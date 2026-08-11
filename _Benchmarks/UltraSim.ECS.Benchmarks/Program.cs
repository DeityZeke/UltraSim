using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.ECS.Benchmarks;

string outputPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..",
    "results",
    "next-ecs-phase1.json"));
string? scenarioFilter = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--output" && i + 1 < args.Length)
        outputPath = Path.GetFullPath(args[++i]);
    else if (args[i] == "--filter" && i + 1 < args.Length)
        scenarioFilter = args[++i];
}

BenchmarkReport report = PrimitiveBenchmarks.Run(scenarioFilter);
string? outputDirectory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrEmpty(outputDirectory))
    Directory.CreateDirectory(outputDirectory);

File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
}));

Console.WriteLine($"ECS Next Phase 1 benchmark: {report.Results.Count} scenarios");
foreach (BenchmarkResult result in report.Results)
{
    Console.WriteLine(
        $"{result.Name}: mean={result.MeanNanosecondsPerOperation:F3} ns/op " +
        $"p95={result.P95Milliseconds:F3} ms alloc={result.MeanAllocatedBytes} B");
}
Console.WriteLine($"Results: {outputPath}");

internal static class PrimitiveBenchmarks
{
    internal static BenchmarkReport Run(string? scenarioFilter)
    {
        var allocate1K = new AllocateFreeFixture(1_024);
        var allocate100K = new AllocateFreeFixture(100_000);
        var live = AliveFixture.CreateLive(100_000, scanCount: 32);
        var stale = AliveFixture.CreateStale(100_000, scanCount: 32);
        var foreign = AliveFixture.CreateForeign(100_000, scanCount: 32);
        var handleScan1M = new HandleScanFixture(1_000_000, scanCount: 8);
        var handleScan10M = new HandleScanFixture(10_000_000, scanCount: 2);
        var schemaLookup = new SchemaLookupFixture(10_000_000);
        using var archetypeLookup = new ArchetypeRegistryLookupFixture(
            lookupOperations: 1_000_000,
            routeOperations: 10_000_000);
        var layoutPlanner = new LayoutPlannerFixture(100_000);
        var archetypeCapacity = new ArchetypeCapacityFixture(1_000_000);
        var worldCapacity = new WorldCapacityFixture(1_000_000);
        using var nativeLifecycle16 = new NativeArenaLifecycleFixture(ChunkLayoutOptions.KiB16, 2_048);
        using var pinnedLifecycle16 = new PinnedArenaLifecycleFixture(ChunkLayoutOptions.KiB16, 2_048);
        using var nativeLifecycle32 = new NativeArenaLifecycleFixture(ChunkLayoutOptions.KiB32, 1_024);
        using var pinnedLifecycle32 = new PinnedArenaLifecycleFixture(ChunkLayoutOptions.KiB32, 1_024);
        using var nativeLifecycle64 = new NativeArenaLifecycleFixture(ChunkLayoutOptions.KiB64, 512);
        using var pinnedLifecycle64 = new PinnedArenaLifecycleFixture(ChunkLayoutOptions.KiB64, 512);
        using var nativeTraversal = new NativeArenaTraversalFixture(100_000);
        using var pinnedTraversal = new PinnedArenaTraversalFixture(100_000);
        using var nativeClear = new NativeArenaClearFixture(wholeChunkOperations: 1_024, activeRowOperations: 100_000);
        using var pinnedClear = new PinnedArenaClearFixture(wholeChunkOperations: 1_024, activeRowOperations: 100_000);
        using var nativeBulkRows = new ArchetypeBulkAddFixture<NativeChunkArena>(100_000);
        using var pinnedBulkRows = new ArchetypeBulkAddFixture<PinnedChunkArena>(100_000);
        using var nativeRowChurn = new ArchetypeSwapChurnFixture<NativeChunkArena>(100_000, 100_000);
        using var pinnedRowChurn = new ArchetypeSwapChurnFixture<PinnedChunkArena>(100_000, 100_000);
        using var nativeEnableToggle = new ArchetypeEnableToggleFixture<NativeChunkArena>(100_000, 1_000_000);
        using var pinnedEnableToggle = new ArchetypeEnableToggleFixture<PinnedChunkArena>(100_000, 1_000_000);
        using var nativeMigration = new ArchetypeMigrationFixture<NativeChunkArena>(100_000);
        using var pinnedMigration = new ArchetypeMigrationFixture<PinnedChunkArena>(100_000);
        using var nativeWorldLifecycle = new EcsWorldLifecycleFixture<NativeChunkArena>(100_000);
        using var pinnedWorldLifecycle = new EcsWorldLifecycleFixture<PinnedChunkArena>(100_000);
        using var nativeBoundAccess = new EcsWorldBoundAccessFixture<NativeChunkArena>(1_000_000);
        using var pinnedBoundAccess = new EcsWorldBoundAccessFixture<PinnedChunkArena>(1_000_000);
        using var publicLookupAccess = new PublicEcsWorldLookupFixture(1_000_000, includeTransition: false);
        using var publicLookupTransition = new PublicEcsWorldLookupFixture(250_000, includeTransition: true);
        using var publicDeferredTransition = new PublicDeferredTransitionFixture(100_000);
        using var publicDeferredTransitionWide = new PublicDeferredTransitionFixture(
            100_000,
            retainWide: true);
        using var publicDeferredTransitionScattered = new PublicDeferredTransitionFixture(
            100_000,
            scatteredSource: true);
        using var publicDeferredTransitionTail8 = new PublicDeferredTransitionFixture(
            8,
            residentEntityCount: 100_000,
            timedCycleCount: 8_192);
        using var publicDeferredTransitionTail32 = new PublicDeferredTransitionFixture(
            32,
            residentEntityCount: 100_000,
            timedCycleCount: 2_048);
        using var publicDeferredTransitionTail128 = new PublicDeferredTransitionFixture(
            128,
            residentEntityCount: 100_000,
            timedCycleCount: 512);
        using var publicDeferredTransitionTail256 = new PublicDeferredTransitionFixture(
            256,
            residentEntityCount: 100_000,
            timedCycleCount: 256);
        using var publicDeferredCreate = new PublicDeferredCreateFixture(100_000);
        using var publicDeferredDestroy = new PublicDeferredDestroyFixture(100_000);
        using var publicDeferredDestroyTail1 = new PublicDeferredDestroyFixture(
            100_000,
            DeferredDestroySelection.Tail1Percent);
        using var publicDeferredDestroyTail10 = new PublicDeferredDestroyFixture(
            100_000,
            DeferredDestroySelection.Tail10Percent);
        using var publicDeferredDestroyTail50 = new PublicDeferredDestroyFixture(
            100_000,
            DeferredDestroySelection.Tail50Percent);
        using var publicDeferredDestroySparse1 = new PublicDeferredDestroyFixture(
            100_000,
            DeferredDestroySelection.Sparse1Percent);
        using var publicDeferredDestroySparse10 = new PublicDeferredDestroyFixture(
            100_000,
            DeferredDestroySelection.Sparse10Percent);
        using var publicDeferredDestroySparse50 = new PublicDeferredDestroyFixture(
            100_000,
            DeferredDestroySelection.Sparse50Percent);
        using var publicBarrierChurnNarrow10 = new PublicBarrierLocalChurnFixture(
            100_000,
            churnPercent: 10,
            wide: false);
        using var publicBarrierChurnWide10 = new PublicBarrierLocalChurnFixture(
            100_000,
            churnPercent: 10,
            wide: true);
        using var publicBarrierChurnNarrow50 = new PublicBarrierLocalChurnFixture(
            100_000,
            churnPercent: 50,
            wide: false);
        using var publicBarrierChurnWide50 = new PublicBarrierLocalChurnFixture(
            100_000,
            churnPercent: 50,
            wide: true);
        using var nativeChunkVisit = new EcsWorldChunkVisitFixture<NativeChunkArena>(100_000);
        using var pinnedChunkVisit = new EcsWorldChunkVisitFixture<PinnedChunkArena>(100_000);
        using var nativeReadWriteQuery = new EcsWorldReadWriteQueryFixture<NativeChunkArena>(100_000);
        using var pinnedReadWriteQuery = new EcsWorldReadWriteQueryFixture<PinnedChunkArena>(100_000);
        using var nativeGeneratedQuery = new EcsWorldGeneratedQueryFixture<NativeChunkArena>(100_000);
        using var pinnedGeneratedQuery = new EcsWorldGeneratedQueryFixture<PinnedChunkArena>(100_000);
        using var nativeGeneratedWriteQuery = new EcsWorldGeneratedWriteQueryFixture<NativeChunkArena>(100_000);
        using var pinnedGeneratedWriteQuery = new EcsWorldGeneratedWriteQueryFixture<PinnedChunkArena>(100_000);
        using var nativeGeneratedReadQuery = new EcsWorldGeneratedReadQueryFixture<NativeChunkArena>(100_000);
        using var pinnedGeneratedReadQuery = new EcsWorldGeneratedReadQueryFixture<PinnedChunkArena>(100_000);
        using var nativeGeneratedWideQuery = new EcsWorldGeneratedWideQueryFixture<NativeChunkArena>(100_000);
        using var pinnedGeneratedWideQuery = new EcsWorldGeneratedWideQueryFixture<PinnedChunkArena>(100_000);
        using var nativeFilteredReadWriteQuery = new EcsWorldFilteredReadWriteQueryFixture<NativeChunkArena>(100_000);
        using var pinnedFilteredReadWriteQuery = new EcsWorldFilteredReadWriteQueryFixture<PinnedChunkArena>(100_000);

        BenchmarkScenario[] scenarios =
        [
            new("EntityPool_AllocateFree_1024", "EntityLifecycle", 1_024, allocate1K.Execute),
            new("EntityPool_AllocateFree_100000", "EntityLifecycle", 100_000, allocate100K.Execute),
            new("EntityPool_IsAlive_Live_100000", "EntityValidation", 100_000, live.Execute),
            new("EntityPool_IsAlive_Stale_100000", "EntityValidation", 100_000, stale.Execute),
            new("EntityPool_IsAlive_Foreign_100000", "EntityValidation", 100_000, foreign.Execute),
            new("EntityHandle_Scan_12Byte_1000000", "HandleLayoutCandidate", 1_000_000, handleScan1M.ExecuteTwelveByte),
            new("EntityHandle_Scan_8Byte_1000000", "HandleLayoutCandidate", 1_000_000, handleScan1M.ExecuteEightByte),
            new("EntityHandle_Scan_12Byte_10000000", "HandleLayoutCandidate", 10_000_000, handleScan10M.ExecuteTwelveByte),
            new("EntityHandle_Scan_8Byte_10000000", "HandleLayoutCandidate", 10_000_000, handleScan10M.ExecuteEightByte),
            new("ComponentId_GeneratedGeneric_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteGeneratedGeneric),
            new("ComponentId_InstanceType_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteInstanceType),
            new("ComponentId_CanonicalKey_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteCanonicalKey),
            new("ComponentId_StableHash_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteStableHash),
            new("ComponentId_PersistenceId_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecutePersistenceId),
            new("ComponentMetadata_ById_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteMetadataById),
            new("ArchetypeRegistry_SignatureLookup_16_1000000", "ArchetypeRegistry", 1_000_000, archetypeLookup.ExecuteSignatureLookup),
            new("ArchetypeRegistry_IdRoute_16_10000000", "ArchetypeRegistry", 10_000_000, archetypeLookup.ExecuteIdRoute),
            new("ChunkLayoutPlanner_Normal_100000", "CapacityPlanning", 100_000, layoutPlanner.Execute),
            new("ArchetypeCapacityPlan_Normal_1000000", "CapacityPlanning", 1_000_000, archetypeCapacity.Execute),
            new("WorldCapacityPlanner_ThreeArchetypes_1000000", "CapacityPlanning", 1_000_000, worldCapacity.Execute),
            new("NativeArena_AcquireClearRelease_16KiB", "ChunkArenaLifecycle", 2_048, nativeLifecycle16.Execute),
            new("PinnedArena_AcquireClearRelease_16KiB", "ChunkArenaLifecycle", 2_048, pinnedLifecycle16.Execute),
            new("NativeArena_AcquireClearRelease_32KiB", "ChunkArenaLifecycle", 1_024, nativeLifecycle32.Execute),
            new("PinnedArena_AcquireClearRelease_32KiB", "ChunkArenaLifecycle", 1_024, pinnedLifecycle32.Execute),
            new("NativeArena_AcquireClearRelease_64KiB", "ChunkArenaLifecycle", 512, nativeLifecycle64.Execute),
            new("PinnedArena_AcquireClearRelease_64KiB", "ChunkArenaLifecycle", 512, pinnedLifecycle64.Execute),
            new("NativeArena_PositionTraversal_100000", "ChunkTraversal", 100_000, nativeTraversal.Execute),
            new("PinnedArena_PositionTraversal_100000", "ChunkTraversal", 100_000, pinnedTraversal.Execute),
            new("NativeArena_ClearWhole_32KiB", "ChunkClear", 1_024, nativeClear.ExecuteWholeChunk),
            new("PinnedArena_ClearWhole_32KiB", "ChunkClear", 1_024, pinnedClear.ExecuteWholeChunk),
            new("NativeArena_ClearActiveRows_32KiB", "ChunkClear", 100_000, nativeClear.ExecuteActiveRows),
            new("PinnedArena_ClearActiveRows_32KiB", "ChunkClear", 100_000, pinnedClear.ExecuteActiveRows),
            new("NativeArchetype_BulkAddDefault_100000", "ArchetypeRows", 100_000, nativeBulkRows.Execute, nativeBulkRows.Prepare),
            new("PinnedArchetype_BulkAddDefault_100000", "ArchetypeRows", 100_000, pinnedBulkRows.Execute, pinnedBulkRows.Prepare),
            new("NativeArchetype_SwapRemoveAdd_100000Cycles", "ArchetypeRows", 100_000, nativeRowChurn.Execute),
            new("PinnedArchetype_SwapRemoveAdd_100000Cycles", "ArchetypeRows", 100_000, pinnedRowChurn.Execute),
            new("NativeArchetype_EnableToggle_1000000", "ArchetypeRows", 1_000_000, nativeEnableToggle.Execute),
            new("PinnedArchetype_EnableToggle_1000000", "ArchetypeRows", 1_000_000, pinnedEnableToggle.Execute),
            new("NativeArchetype_MigrationCycle_100000", "ArchetypeMigration", 100_000, nativeMigration.Execute),
            new("PinnedArchetype_MigrationCycle_100000", "ArchetypeMigration", 100_000, pinnedMigration.Execute),
            new("NativeWorld_CreateTransitionDestroy_100000", "WorldLifecycle", 100_000, nativeWorldLifecycle.Execute),
            new("PinnedWorld_CreateTransitionDestroy_100000", "WorldLifecycle", 100_000, pinnedWorldLifecycle.Execute),
            new("NativeWorld_BoundReadWriteEnable_1000000", "WorldComponentAccess", 1_000_000, nativeBoundAccess.Execute),
            new("PinnedWorld_BoundReadWriteEnable_1000000", "WorldComponentAccess", 1_000_000, pinnedBoundAccess.Execute),
            new("PublicWorld_PreparedLookupReadWriteEnable_1000000", "PublicWorldComponentLookup", 1_000_000, publicLookupAccess.Execute),
            new("PublicWorld_BoundedAccessJobReadWriteEnable_1000000", "PublicWorldComponentLookup", 1_000_000, publicLookupAccess.ExecutePrepared),
            new("PublicWorld_BoundedAccessJobReadWriteEnableTransition_250000", "PublicWorldComponentLookup", 250_000, publicLookupTransition.ExecutePrepared),
            new("PublicWorld_DeferredTransitionScalarOracle_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredTransition.ExecuteScalarOracle, publicDeferredTransition.Prepare),
            new("PublicWorld_DeferredTransitionBatch_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredTransition.Execute, publicDeferredTransition.Prepare),
            new("PublicWorld_DeferredTransitionPlaybackOnlyScalarOracle_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredTransition.ExecuteRecordedScalarOracle, publicDeferredTransition.PrepareRecorded),
            new("PublicWorld_DeferredTransitionPlaybackOnlyGrouped_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredTransition.ExecuteRecorded, publicDeferredTransition.PrepareRecorded),
            new("PublicWorld_DeferredTransitionPlaybackOnlyScalarOracle_WideRetained_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredTransitionWide.ExecuteRecordedScalarOracle, publicDeferredTransitionWide.PrepareRecorded),
            new("PublicWorld_DeferredTransitionPlaybackOnlyGrouped_WideRetained_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredTransitionWide.ExecuteRecorded, publicDeferredTransitionWide.PrepareRecorded),
            new("PublicWorld_DeferredTransitionPlaybackOnlyScalarOracle_ScatteredSource_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredTransitionScattered.ExecuteRecordedScalarOracle, publicDeferredTransitionScattered.PrepareRecorded),
            new("PublicWorld_DeferredTransitionPlaybackOnlyGrouped_ScatteredSource_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredTransitionScattered.ExecuteRecorded, publicDeferredTransitionScattered.PrepareRecorded),
            new("PublicWorld_DeferredTransitionRoundTripScalarOracle_Tail8_Resident100000_131072Ops", "PublicWorldStructuralCompaction", 131_072, publicDeferredTransitionTail8.ExecuteRoundTripsScalarOracle),
            new("PublicWorld_DeferredTransitionRoundTripGrouped_Tail8_Resident100000_131072Ops", "PublicWorldStructuralCompaction", 131_072, publicDeferredTransitionTail8.ExecuteRoundTrips),
            new("PublicWorld_DeferredTransitionRoundTripScalarOracle_Tail32_Resident100000_131072Ops", "PublicWorldStructuralCompaction", 131_072, publicDeferredTransitionTail32.ExecuteRoundTripsScalarOracle),
            new("PublicWorld_DeferredTransitionRoundTripGrouped_Tail32_Resident100000_131072Ops", "PublicWorldStructuralCompaction", 131_072, publicDeferredTransitionTail32.ExecuteRoundTrips),
            new("PublicWorld_DeferredTransitionRoundTripScalarOracle_Tail128_Resident100000_131072Ops", "PublicWorldStructuralCompaction", 131_072, publicDeferredTransitionTail128.ExecuteRoundTripsScalarOracle),
            new("PublicWorld_DeferredTransitionRoundTripGrouped_Tail128_Resident100000_131072Ops", "PublicWorldStructuralCompaction", 131_072, publicDeferredTransitionTail128.ExecuteRoundTrips),
            new("PublicWorld_DeferredTransitionRoundTripScalarOracle_Tail256_Resident100000_131072Ops", "PublicWorldStructuralCompaction", 131_072, publicDeferredTransitionTail256.ExecuteRoundTripsScalarOracle),
            new("PublicWorld_DeferredTransitionRoundTripGrouped_Tail256_Resident100000_131072Ops", "PublicWorldStructuralCompaction", 131_072, publicDeferredTransitionTail256.ExecuteRoundTrips),
            new("PublicWorld_DeferredCreateDefaultBatch_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredCreate.Execute, publicDeferredCreate.Prepare),
            new("PublicWorld_DeferredDestroyBatch_100000", "PublicWorldStructuralCommands", 100_000, publicDeferredDestroy.Execute, publicDeferredDestroy.Prepare),
            new("PublicWorld_DeferredDestroyPlaybackOnlyScalarOracle_Whole_100000", "PublicWorldStructuralDestroy", 100_000, publicDeferredDestroy.ExecuteRecordedScalarOracle, publicDeferredDestroy.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyGrouped_Whole_100000", "PublicWorldStructuralDestroy", 100_000, publicDeferredDestroy.ExecuteRecorded, publicDeferredDestroy.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyScalarOracle_Tail1PctOf100000_1000Ops", "PublicWorldStructuralDestroy", 1_000, publicDeferredDestroyTail1.ExecuteRecordedScalarOracle, publicDeferredDestroyTail1.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyGrouped_Tail1PctOf100000_1000Ops", "PublicWorldStructuralDestroy", 1_000, publicDeferredDestroyTail1.ExecuteRecorded, publicDeferredDestroyTail1.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyScalarOracle_Tail10PctOf100000_10000Ops", "PublicWorldStructuralDestroy", 10_000, publicDeferredDestroyTail10.ExecuteRecordedScalarOracle, publicDeferredDestroyTail10.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyGrouped_Tail10PctOf100000_10000Ops", "PublicWorldStructuralDestroy", 10_000, publicDeferredDestroyTail10.ExecuteRecorded, publicDeferredDestroyTail10.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyScalarOracle_Tail50PctOf100000_50000Ops", "PublicWorldStructuralDestroy", 50_000, publicDeferredDestroyTail50.ExecuteRecordedScalarOracle, publicDeferredDestroyTail50.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyGrouped_Tail50PctOf100000_50000Ops", "PublicWorldStructuralDestroy", 50_000, publicDeferredDestroyTail50.ExecuteRecorded, publicDeferredDestroyTail50.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyScalarOracle_Sparse1PctOf100000_1000Ops", "PublicWorldStructuralDestroy", 1_000, publicDeferredDestroySparse1.ExecuteRecordedScalarOracle, publicDeferredDestroySparse1.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyGrouped_Sparse1PctOf100000_1000Ops", "PublicWorldStructuralDestroy", 1_000, publicDeferredDestroySparse1.ExecuteRecorded, publicDeferredDestroySparse1.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyScalarOracle_Sparse10PctOf100000_10000Ops", "PublicWorldStructuralDestroy", 10_000, publicDeferredDestroySparse10.ExecuteRecordedScalarOracle, publicDeferredDestroySparse10.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyGrouped_Sparse10PctOf100000_10000Ops", "PublicWorldStructuralDestroy", 10_000, publicDeferredDestroySparse10.ExecuteRecorded, publicDeferredDestroySparse10.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyScalarOracle_Sparse50PctOf100000_50000Ops", "PublicWorldStructuralDestroy", 50_000, publicDeferredDestroySparse50.ExecuteRecordedScalarOracle, publicDeferredDestroySparse50.PrepareRecorded),
            new("PublicWorld_DeferredDestroyPlaybackOnlyGrouped_Sparse50PctOf100000_50000Ops", "PublicWorldStructuralDestroy", 50_000, publicDeferredDestroySparse50.ExecuteRecorded, publicDeferredDestroySparse50.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyScalarOracle_Narrow_Sparse10PctOf100000_20000Commands", "PublicWorldStructuralHoleReuse", 20_000, publicBarrierChurnNarrow10.ExecuteRecordedScalarOracle, publicBarrierChurnNarrow10.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyPackedV29Control_Narrow_Sparse10PctOf100000_20000Commands", "PublicWorldStructuralHoleReuse", 20_000, publicBarrierChurnNarrow10.ExecuteRecordedPackedControl, publicBarrierChurnNarrow10.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyGrouped_Narrow_Sparse10PctOf100000_20000Commands", "PublicWorldStructuralHoleReuse", 20_000, publicBarrierChurnNarrow10.ExecuteRecorded, publicBarrierChurnNarrow10.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyScalarOracle_Wide512_Sparse10PctOf100000_20000Commands", "PublicWorldStructuralHoleReuse", 20_000, publicBarrierChurnWide10.ExecuteRecordedScalarOracle, publicBarrierChurnWide10.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyPackedV29Control_Wide512_Sparse10PctOf100000_20000Commands", "PublicWorldStructuralHoleReuse", 20_000, publicBarrierChurnWide10.ExecuteRecordedPackedControl, publicBarrierChurnWide10.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyGrouped_Wide512_Sparse10PctOf100000_20000Commands", "PublicWorldStructuralHoleReuse", 20_000, publicBarrierChurnWide10.ExecuteRecorded, publicBarrierChurnWide10.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyScalarOracle_Narrow_Sparse50PctOf100000_100000Commands", "PublicWorldStructuralHoleReuse", 100_000, publicBarrierChurnNarrow50.ExecuteRecordedScalarOracle, publicBarrierChurnNarrow50.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyPackedV29Control_Narrow_Sparse50PctOf100000_100000Commands", "PublicWorldStructuralHoleReuse", 100_000, publicBarrierChurnNarrow50.ExecuteRecordedPackedControl, publicBarrierChurnNarrow50.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyGrouped_Narrow_Sparse50PctOf100000_100000Commands", "PublicWorldStructuralHoleReuse", 100_000, publicBarrierChurnNarrow50.ExecuteRecorded, publicBarrierChurnNarrow50.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyScalarOracle_Wide512_Sparse50PctOf100000_100000Commands", "PublicWorldStructuralHoleReuse", 100_000, publicBarrierChurnWide50.ExecuteRecordedScalarOracle, publicBarrierChurnWide50.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyPackedV29Control_Wide512_Sparse50PctOf100000_100000Commands", "PublicWorldStructuralHoleReuse", 100_000, publicBarrierChurnWide50.ExecuteRecordedPackedControl, publicBarrierChurnWide50.PrepareRecorded),
            new("PublicWorld_BarrierChurnPlaybackOnlyGrouped_Wide512_Sparse50PctOf100000_100000Commands", "PublicWorldStructuralHoleReuse", 100_000, publicBarrierChurnWide50.ExecuteRecorded, publicBarrierChurnWide50.PrepareRecorded),
            new("NativeWorld_BoundChunkRead_100000", "WorldChunkQuery", 100_000, nativeChunkVisit.ExecuteRead),
            new("PinnedWorld_BoundChunkRead_100000", "WorldChunkQuery", 100_000, pinnedChunkVisit.ExecuteRead),
            new("NativeWorld_BoundChunkWrite_100000", "WorldChunkQuery", 100_000, nativeChunkVisit.ExecuteWrite),
            new("PinnedWorld_BoundChunkWrite_100000", "WorldChunkQuery", 100_000, pinnedChunkVisit.ExecuteWrite),
            new("NativeWorld_ReadPositionWriteVelocity_2Archetypes_100000", "WorldQueryPlan", 100_000, nativeReadWriteQuery.Execute),
            new("PinnedWorld_ReadPositionWriteVelocity_2Archetypes_100000", "WorldQueryPlan", 100_000, pinnedReadWriteQuery.Execute),
            new("NativeWorld_Generated2R1W_PositionWideVelocity_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, nativeGeneratedQuery.Execute),
            new("PinnedWorld_Generated2R1W_PositionWideVelocity_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, pinnedGeneratedQuery.Execute),
            new("NativeWorld_Generated1R2W_PositionWideVelocity_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, nativeGeneratedWriteQuery.Execute),
            new("PinnedWorld_Generated1R2W_PositionWideVelocity_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, pinnedGeneratedWriteQuery.Execute),
            new("NativeWorld_Generated2R0W_PositionWide_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, nativeGeneratedReadQuery.Execute),
            new("PinnedWorld_Generated2R0W_PositionWide_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, pinnedGeneratedReadQuery.Execute),
            new("NativeWorld_Generated3R1W_PositionWideScalarVelocity_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, nativeGeneratedWideQuery.ExecuteThreeRead),
            new("PinnedWorld_Generated3R1W_PositionWideScalarVelocity_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, pinnedGeneratedWideQuery.ExecuteThreeRead),
            new("NativeWorld_Generated2R2W_PositionScalarWideVelocity_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, nativeGeneratedWideQuery.ExecuteBalanced),
            new("PinnedWorld_Generated2R2W_PositionScalarWideVelocity_2Archetypes_100000", "WorldGeneratedQueryPlan", 100_000, pinnedGeneratedWideQuery.ExecuteBalanced),
            new("NativeWorld_ChangeVersionScan_UnchangedPosition_2Archetypes_100000", "WorldChangeVersion", 100_000, nativeGeneratedWideQuery.ExecuteUnchangedVersionScan),
            new("PinnedWorld_ChangeVersionScan_UnchangedPosition_2Archetypes_100000", "WorldChangeVersion", 100_000, pinnedGeneratedWideQuery.ExecuteUnchangedVersionScan),
            new("NativeWorld_ChangedRead_UnchangedScalar_2Archetypes_100000", "WorldChangeCursor", 100_000, nativeGeneratedWideQuery.ExecuteUnchangedChangedRead),
            new("PinnedWorld_ChangedRead_UnchangedScalar_2Archetypes_100000", "WorldChangeCursor", 100_000, pinnedGeneratedWideQuery.ExecuteUnchangedChangedRead),
            new("NativeWorld_ChangedRead_AllChangedScalar_2Archetypes_100000", "WorldChangeCursor", 100_000, nativeGeneratedWideQuery.ExecuteAllChangedRead, nativeGeneratedWideQuery.PrepareAllChangedRead),
            new("PinnedWorld_ChangedRead_AllChangedScalar_2Archetypes_100000", "WorldChangeCursor", 100_000, pinnedGeneratedWideQuery.ExecuteAllChangedRead, pinnedGeneratedWideQuery.PrepareAllChangedRead),
            new("NativeWorld_Changed2Any_UnchangedScalarVelocity_2Archetypes_100000", "WorldMultiChangeCursor", 100_000, nativeGeneratedWideQuery.ExecuteUnchangedMultiAny, nativeGeneratedWideQuery.PrepareUnchangedMultiAny),
            new("PinnedWorld_Changed2Any_UnchangedScalarVelocity_2Archetypes_100000", "WorldMultiChangeCursor", 100_000, pinnedGeneratedWideQuery.ExecuteUnchangedMultiAny, pinnedGeneratedWideQuery.PrepareUnchangedMultiAny),
            new("NativeWorld_Changed2All_UnchangedScalarVelocity_2Archetypes_100000", "WorldMultiChangeCursor", 100_000, nativeGeneratedWideQuery.ExecuteUnchangedMultiAll, nativeGeneratedWideQuery.PrepareUnchangedMultiAll),
            new("PinnedWorld_Changed2All_UnchangedScalarVelocity_2Archetypes_100000", "WorldMultiChangeCursor", 100_000, pinnedGeneratedWideQuery.ExecuteUnchangedMultiAll, pinnedGeneratedWideQuery.PrepareUnchangedMultiAll),
            new("NativeWorld_Changed2Any_AllChangedScalarVelocity_2Archetypes_100000", "WorldMultiChangeCursor", 100_000, nativeGeneratedWideQuery.ExecuteAllChangedMultiAny, nativeGeneratedWideQuery.PrepareAllChangedMultiAny),
            new("PinnedWorld_Changed2Any_AllChangedScalarVelocity_2Archetypes_100000", "WorldMultiChangeCursor", 100_000, pinnedGeneratedWideQuery.ExecuteAllChangedMultiAny, pinnedGeneratedWideQuery.PrepareAllChangedMultiAny),
            new("NativeWorld_Changed2All_AllChangedScalarVelocity_2Archetypes_100000", "WorldMultiChangeCursor", 100_000, nativeGeneratedWideQuery.ExecuteAllChangedMultiAll, nativeGeneratedWideQuery.PrepareAllChangedMultiAll),
            new("PinnedWorld_Changed2All_AllChangedScalarVelocity_2Archetypes_100000", "WorldMultiChangeCursor", 100_000, pinnedGeneratedWideQuery.ExecuteAllChangedMultiAll, pinnedGeneratedWideQuery.PrepareAllChangedMultiAll),
            new("NativeWorld_ChangedRows_UnchangedScalar_2Archetypes_100000", "WorldChangeRows", 100_000, nativeGeneratedWideQuery.ExecuteUnchangedChangedRows, nativeGeneratedWideQuery.PrepareUnchangedChangedRows),
            new("PinnedWorld_ChangedRows_UnchangedScalar_2Archetypes_100000", "WorldChangeRows", 100_000, pinnedGeneratedWideQuery.ExecuteUnchangedChangedRows, pinnedGeneratedWideQuery.PrepareUnchangedChangedRows),
            new("NativeWorld_ChangedRows_OneChangedScalar_2Archetypes_100000", "WorldChangeRows", 100_000, nativeGeneratedWideQuery.ExecuteOneChangedRow, nativeGeneratedWideQuery.PrepareOneChangedRow),
            new("PinnedWorld_ChangedRows_OneChangedScalar_2Archetypes_100000", "WorldChangeRows", 100_000, pinnedGeneratedWideQuery.ExecuteOneChangedRow, pinnedGeneratedWideQuery.PrepareOneChangedRow),
            new("NativeWorld_FilteredReadPositionWriteVelocity_Dense_100000", "WorldQueryFilter", 100_000, nativeFilteredReadWriteQuery.Execute, nativeFilteredReadWriteQuery.PrepareDense),
            new("PinnedWorld_FilteredReadPositionWriteVelocity_Dense_100000", "WorldQueryFilter", 100_000, pinnedFilteredReadWriteQuery.Execute, pinnedFilteredReadWriteQuery.PrepareDense),
            new("NativeWorld_FilteredReadPositionWriteVelocity_Alternating_100000", "WorldQueryFilter", 100_000, nativeFilteredReadWriteQuery.Execute, nativeFilteredReadWriteQuery.PrepareAlternating),
            new("PinnedWorld_FilteredReadPositionWriteVelocity_Alternating_100000", "WorldQueryFilter", 100_000, pinnedFilteredReadWriteQuery.Execute, pinnedFilteredReadWriteQuery.PrepareAlternating),
        ];

        var results = new List<BenchmarkResult>(scenarios.Length);
        foreach (BenchmarkScenario scenario in scenarios)
        {
            if (!MatchesFilter(scenario, scenarioFilter))
                continue;
            results.Add(RunScenario(scenario));
        }
        RunPermanentExactSuffixScenarios(results, scenarioFilter);
        if (results.Count == 0 && !string.IsNullOrWhiteSpace(scenarioFilter))
            throw new ArgumentException($"No benchmark scenario matched filter '{scenarioFilter}'.", nameof(scenarioFilter));

        return new BenchmarkReport
        {
            SuiteName = "UltraSim.ECS.Next.Phase1",
            Generation = "Next",
            SemanticProfile = EcsKernelVersion.SemanticProfile,
            ScenarioFilter = scenarioFilter,
            RuntimeVersion = Environment.Version.ToString(),
            Configuration = GetConfiguration(),
            ProcessorCount = Environment.ProcessorCount,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OperatingSystem = RuntimeInformation.OSDescription,
            EntitySizeBytes = Marshal.SizeOf<Entity>(),
            ComponentIdSizeBytes = Marshal.SizeOf<ComponentId>(),
            ComponentMetadataSizeBytes = Marshal.SizeOf<ComponentMetadata>(),
            ChangeVersionSizeBytes = Marshal.SizeOf<ChangeVersion>(),
            ChangeCursorSizeBytes = Marshal.SizeOf<ChangeCursor>(),
            ArchetypeHandleSizeBytes = Marshal.SizeOf<ArchetypeHandle>(),
            ArchetypeTransitionSizeBytes = Marshal.SizeOf<ArchetypeTransition>(),
            DeferredEntitySizeBytes = Marshal.SizeOf<DeferredEntity>(),
            StructuralCommandRecordSizeBytes = Unsafe.SizeOf<StructuralCommandRecord>(),
            StructuralPlaybackResultSizeBytes = Marshal.SizeOf<StructuralPlaybackResult>(),
            StructuralCommandReservedBytes = publicDeferredTransition.CommandReservedBytes,
            PublicLookupRouteBytes = publicLookupTransition.RouteBytes,
            ExactChangedRowsMetadataBytes = nativeGeneratedWideQuery.ExactChangedRowsMetadataBytes,
            SchemaIdSizeBytes = Marshal.SizeOf<SchemaId>(),
            EstimatedEntityPoolMetadataBytesPerSlot = new EntityCapacity(1).EstimatedMetadataBytes,
            TimestampUtc = DateTimeOffset.UtcNow,
            LayoutPlans = LayoutReportBuilder.Create(),
            ArenaFacts = ArenaFactBuilder.Create(),
            ArchetypeFacts = ArchetypeFactBuilder.Create(),
            Results = results,
        };
    }

    private static void RunPermanentExactSuffixScenarios(
        List<BenchmarkResult> results,
        string? scenarioFilter)
    {
        RunPermanentExactSuffixScenarioPair(
            results,
            scenarioFilter,
            suffixCount: 257,
            label: "Normal_257Of100000",
            retainWide: false,
            trackExactRows: false);
        RunPermanentExactSuffixScenarioPair(
            results,
            scenarioFilter,
            suffixCount: 10_000,
            label: "Normal_10000Of100000",
            retainWide: false,
            trackExactRows: false);
        RunPermanentExactSuffixScenarioPair(
            results,
            scenarioFilter,
            suffixCount: 50_000,
            label: "Normal_50000Of100000",
            retainWide: false,
            trackExactRows: false);
        RunPermanentExactSuffixScenarioPair(
            results,
            scenarioFilter,
            suffixCount: 10_000,
            label: "WideRetained_10000Of100000",
            retainWide: true,
            trackExactRows: false);
        RunPermanentExactSuffixScenarioPair(
            results,
            scenarioFilter,
            suffixCount: 50_000,
            label: "WideRetained_50000Of100000",
            retainWide: true,
            trackExactRows: false);
        RunPermanentExactSuffixScenarioPair(
            results,
            scenarioFilter,
            suffixCount: 10_000,
            label: "WideRetainedExactHistory_10000Of100000",
            retainWide: true,
            trackExactRows: true);
    }

    private static void RunPermanentExactSuffixScenarioPair(
        List<BenchmarkResult> results,
        string? scenarioFilter,
        int suffixCount,
        string label,
        bool retainWide,
        bool trackExactRows)
    {
        string endToEndName =
            $"PublicWorld_ExactPackedSuffixTransition_EndToEnd_{label}";
        string playbackName =
            $"PublicWorld_ExactPackedSuffixTransition_Playback_{label}";
        var endToEnd = new BenchmarkScenario(
            endToEndName,
            "PublicWorldStructuralSuffixTransition",
            suffixCount,
            static () => default);
        var playback = new BenchmarkScenario(
            playbackName,
            "PublicWorldStructuralSuffixTransition",
            suffixCount,
            static () => default);
        bool runEndToEnd = MatchesFilter(endToEnd, scenarioFilter);
        bool runPlayback = MatchesFilter(playback, scenarioFilter);
        if (!runEndToEnd && !runPlayback)
            return;

        using var fixture = new PublicDeferredTransitionFixture(
            suffixCount,
            retainWide,
            residentEntityCount: 100_000,
            packedSuffix: true,
            trackExactRows: trackExactRows);
        if (runEndToEnd)
        {
            results.Add(RunScenario(new BenchmarkScenario(
                endToEndName,
                endToEnd.Category,
                suffixCount,
                fixture.Execute,
                fixture.Prepare)));
        }
        if (runPlayback)
        {
            results.Add(RunScenario(new BenchmarkScenario(
                playbackName,
                playback.Category,
                suffixCount,
                fixture.ExecuteRecorded,
                fixture.PrepareRecorded)));
        }
    }

    private static bool MatchesFilter(in BenchmarkScenario scenario, string? filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        scenario.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        scenario.Category.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static BenchmarkResult RunScenario(BenchmarkScenario scenario)
    {
        const int warmupCount = 8;
        const int iterationCount = 31;

        for (int i = 0; i < warmupCount; i++)
        {
            scenario.Prepare?.Invoke();
            _ = scenario.Execute();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var elapsedMilliseconds = new double[iterationCount];
        var allocatedBytes = new long[iterationCount];
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long checksum = 0;
        long operations = 0;
        StructuralPlaybackDiagnostics diagnostics = default;

        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            scenario.Prepare?.Invoke();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            ScenarioMeasurement measurement = scenario.Execute();
            long stopped = Stopwatch.GetTimestamp();
            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            operations = measurement.Operations;
            checksum ^= measurement.Checksum;
            diagnostics = measurement.Diagnostics;
            elapsedMilliseconds[iteration] = Stopwatch.GetElapsedTime(started, stopped).TotalMilliseconds;
            allocatedBytes[iteration] = allocatedAfter - allocatedBefore;
        }

        double[] sorted = elapsedMilliseconds.ToArray();
        Array.Sort(sorted);
        double meanMilliseconds = elapsedMilliseconds.Average();

        return new BenchmarkResult
        {
            Name = scenario.Name,
            Category = scenario.Category,
            Comparable = false,
            Scale = scenario.Scale,
            WarmupCount = warmupCount,
            IterationCount = iterationCount,
            OperationsPerIteration = operations,
            MeanMilliseconds = meanMilliseconds,
            MinMilliseconds = sorted[0],
            P50Milliseconds = Percentile(sorted, 0.50),
            P95Milliseconds = Percentile(sorted, 0.95),
            P99Milliseconds = Percentile(sorted, 0.99),
            MaxMilliseconds = sorted[^1],
            MeanNanosecondsPerOperation = meanMilliseconds * 1_000_000d / operations,
            P95NanosecondsPerOperation =
                Percentile(sorted, 0.95) * 1_000_000d / operations,
            OperationsPerSecond = operations / (meanMilliseconds / 1000d),
            MeanAllocatedBytes = (long)allocatedBytes.Average(),
            Gen0Collections = GC.CollectionCount(0) - gen0Before,
            Gen1Collections = GC.CollectionCount(1) - gen1Before,
            Gen2Collections = GC.CollectionCount(2) - gen2Before,
            Checksum = checksum,
            ExactWholeSourceResets = diagnostics.ExactWholeSourceResets,
            ExactSuffixShrinks = diagnostics.ExactSuffixShrinks,
            ExactSuffixCommands = diagnostics.ExactSuffixCommands,
            ExactSuffixBlocksStreamed = diagnostics.ExactSuffixBlocksStreamed,
            ExactSuffixRowsBulkShrunk = diagnostics.ExactSuffixRowsBulkShrunk,
            ExactSuffixDonorRelocations = diagnostics.ExactSuffixDonorRelocations,
            FailedExactSuffixCandidates = diagnostics.FailedExactSuffixCandidates,
        };
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static string GetConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private sealed class AllocateFreeFixture
    {
        private readonly EntityPool _pool;
        private readonly Entity[] _entities;

        internal AllocateFreeFixture(int capacity)
        {
            _pool = new EntityPool(WorldId.Create(), new EntityCapacity(capacity));
            _entities = new Entity[capacity];
            if (_pool.Allocate(_entities) != capacity)
                throw new InvalidOperationException("Fixture initialization failed.");
        }

        internal ScenarioMeasurement Execute()
        {
            int freed = _pool.Free(_entities);
            int allocated = _pool.Allocate(_entities);
            long checksum = _entities[0].Generation ^ _entities[^1].Generation ^ (uint)_pool.AliveCount;
            return new ScenarioMeasurement(freed + allocated, checksum);
        }
    }

    private sealed class AliveFixture
    {
        private readonly EntityPool _owner;
        private readonly Entity[] _entities;
        private readonly int _scanCount;

        private AliveFixture(EntityPool owner, Entity[] entities, int scanCount)
        {
            _owner = owner;
            _entities = entities;
            _scanCount = scanCount;
        }

        internal static AliveFixture CreateLive(int count, int scanCount)
        {
            var pool = new EntityPool(WorldId.Create(), new EntityCapacity(count));
            var entities = new Entity[count];
            pool.Allocate(entities);
            return new AliveFixture(pool, entities, scanCount);
        }

        internal static AliveFixture CreateStale(int count, int scanCount)
        {
            var pool = new EntityPool(WorldId.Create(), new EntityCapacity(count));
            var stale = new Entity[count];
            var live = new Entity[count];
            pool.Allocate(stale);
            pool.Free(stale);
            pool.Allocate(live);
            return new AliveFixture(pool, stale, scanCount);
        }

        internal static AliveFixture CreateForeign(int count, int scanCount)
        {
            var owner = new EntityPool(WorldId.Create(), new EntityCapacity(count));
            var foreign = new EntityPool(WorldId.Create(), new EntityCapacity(count));
            var entities = new Entity[count];
            foreign.Allocate(entities);
            return new AliveFixture(owner, entities, scanCount);
        }

        internal ScenarioMeasurement Execute()
        {
            long alive = 0;
            for (int scan = 0; scan < _scanCount; scan++)
            {
                for (int i = 0; i < _entities.Length; i++)
                {
                    if (_owner.IsAlive(_entities[i]))
                        alive++;
                }
            }

            return new ScenarioMeasurement((long)_entities.Length * _scanCount, alive);
        }
    }

    private sealed class HandleScanFixture
    {
        private readonly Entity[] _entities;
        private readonly PackedEntityCandidate[] _packed;
        private readonly int _scanCount;

        internal HandleScanFixture(int count, int scanCount)
        {
            var worldId = new WorldId(1);
            _entities = new Entity[count];
            _packed = new PackedEntityCandidate[count];
            _scanCount = scanCount;

            for (int i = 0; i < count; i++)
            {
                uint index = (uint)i;
                uint generation = (uint)(i & 0x00FF_FFFF) + 1;
                _entities[i] = new Entity(worldId, index, generation);
                _packed[i] = PackedEntityCandidate.Create(world: 1, index, generation);
            }
        }

        internal ScenarioMeasurement ExecuteTwelveByte()
        {
            ulong checksum = 0;
            for (int scan = 0; scan < _scanCount; scan++)
            {
                for (int i = 0; i < _entities.Length; i++)
                    checksum += _entities[i].Index + _entities[i].Generation + _entities[i].World.Value;
            }

            return new ScenarioMeasurement((long)_entities.Length * _scanCount, unchecked((long)checksum));
        }

        internal ScenarioMeasurement ExecuteEightByte()
        {
            ulong checksum = 0;
            for (int scan = 0; scan < _scanCount; scan++)
            {
                for (int i = 0; i < _packed.Length; i++)
                    checksum += _packed[i].Index + _packed[i].Generation + _packed[i].World;
            }

            return new ScenarioMeasurement((long)_packed.Length * _scanCount, unchecked((long)checksum));
        }
    }

    private sealed class SchemaLookupFixture
    {
        private readonly ComponentSchema _schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
        private readonly ComponentId _velocityId =
            UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>();
        private readonly int _operations;
        private readonly ulong _stableHash = StableHash64.Compute("benchmark.velocity");

        internal SchemaLookupFixture(int operations)
        {
            _operations = operations;
        }

        internal ScenarioMeasurement ExecuteGeneratedGeneric()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
                checksum += UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>().Value;
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecuteInstanceType()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                _schema.TryGetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>(out ComponentId id);
                checksum += id.Value;
            }
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecuteCanonicalKey()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                _schema.TryGetComponentId("benchmark.velocity", out ComponentId id);
                checksum += id.Value;
            }
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecuteStableHash()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                _schema.TryGetComponentId(_stableHash, out ComponentId id);
                checksum += id.Value;
            }
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecutePersistenceId()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                _schema.TryGetComponentIdByPersistenceId(1, out ComponentId id);
                checksum += id.Value;
            }
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecuteMetadataById()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
                checksum += _schema.GetMetadata(_velocityId).Size;
            return new ScenarioMeasurement(_operations, checksum);
        }
    }

    private sealed class ArchetypeRegistryLookupFixture : IDisposable
    {
        private const int SignatureCount = 16;

        private readonly ArchetypeRegistry<NativeChunkArena> _registry;
        private readonly ComponentId[][] _signatures;
        private readonly ArchetypeId[] _ids;
        private readonly int _lookupOperations;
        private readonly int _routeOperations;

        internal ArchetypeRegistryLookupFixture(int lookupOperations, int routeOperations)
        {
            ComponentSchema schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
            _registry = new ArchetypeRegistry<NativeChunkArena>(
                schema,
                SignatureCount,
                static capacity => new NativeChunkArena(capacity));
            _signatures = new ComponentId[SignatureCount][];
            _ids = new ArchetypeId[SignatureCount];
            for (int mask = 0; mask < SignatureCount; mask++)
            {
                ComponentId[] signature = CreateSignature(mask, schema.ComponentCount);
                _signatures[mask] = signature;
                _ids[mask] = _registry.Register(signature, ChunkLayoutOptions.KiB16, maximumChunks: 1);
            }
            _registry.Seal();
            _lookupOperations = lookupOperations;
            _routeOperations = routeOperations;
        }

        internal ScenarioMeasurement ExecuteSignatureLookup()
        {
            long checksum = 0;
            for (int i = 0; i < _lookupOperations; i++)
            {
                if (!_registry.TryGetId(_signatures[i & (SignatureCount - 1)], out ArchetypeId id))
                    throw new InvalidOperationException("A registered archetype signature was not found.");
                checksum += id.Value;
            }

            return new ScenarioMeasurement(_lookupOperations, checksum);
        }

        internal ScenarioMeasurement ExecuteIdRoute()
        {
            long checksum = 0;
            for (int i = 0; i < _routeOperations; i++)
            {
                ArchetypeStorage<NativeChunkArena> storage =
                    _registry.GetStorage(_ids[i & (SignatureCount - 1)]);
                checksum = unchecked(checksum + (long)storage.SignatureHash);
            }

            return new ScenarioMeasurement(_routeOperations, checksum);
        }

        public void Dispose() => _registry.Dispose();

        private static ComponentId[] CreateSignature(int mask, int componentCount)
        {
            var signature = new ComponentId[componentCount];
            int count = 0;
            for (ushort component = 0; component < componentCount; component++)
            {
                if ((mask & (1 << component)) != 0)
                    signature[count++] = new ComponentId(component);
            }

            if (count == signature.Length)
                return signature;
            return signature.AsSpan(0, count).ToArray();
        }
    }

    private sealed class LayoutPlannerFixture
    {
        private readonly ComponentSchema _schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
        private readonly ComponentId[] _signature = LayoutReportBuilder.CreateNormalSignature();
        private readonly ComponentColumnLayout[] _columns;
        private readonly int _operations;

        internal LayoutPlannerFixture(int operations)
        {
            _operations = operations;
            _columns = new ComponentColumnLayout[_signature.Length];
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                ChunkLayoutPlan plan = ChunkLayoutPlanner.Plan(
                    _schema,
                    _signature,
                    ChunkLayoutOptions.KiB32,
                    _columns);
                checksum += plan.RowCapacity + plan.BytesUsed + _columns[0].DataOffsetBytes;
            }

            return new ScenarioMeasurement(_operations, checksum);
        }
    }

    private sealed class ArchetypeCapacityFixture
    {
        private readonly ChunkLayoutPlan _layout;
        private readonly int _operations;

        internal ArchetypeCapacityFixture(int operations)
        {
            _operations = operations;
            ComponentSchema schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
            ComponentId[] signature = LayoutReportBuilder.CreateNormalSignature();
            var columns = new ComponentColumnLayout[signature.Length];
            _layout = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB32, columns);
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                var plan = new ArchetypeCapacityPlan(_layout, 100_000 + (i & 1));
                checksum += plan.RequiredChunkCount + plan.ReservedChunkBytes;
            }

            return new ScenarioMeasurement(_operations, checksum);
        }
    }

    private sealed class WorldCapacityFixture
    {
        private readonly ArchetypeCapacityPlan[] _archetypes;
        private readonly EntityCapacity _entityCapacity = new(500_000);
        private readonly WorldArenaReservations _arenas = new(8 * 1024 * 1024, 4 * 1024 * 1024, 2 * 1024 * 1024);
        private readonly int _operations;

        internal WorldCapacityFixture(int operations)
        {
            _operations = operations;
            ComponentSchema schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
            ComponentId[] signature = LayoutReportBuilder.CreateNormalSignature();
            var columns = new ComponentColumnLayout[signature.Length];
            ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB32, columns);
            _archetypes =
            [
                new ArchetypeCapacityPlan(layout, 100_000),
                new ArchetypeCapacityPlan(layout, 50_000),
                new ArchetypeCapacityPlan(layout, 25_000),
            ];
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                WorldCapacityPlan plan = WorldCapacityPlanner.Plan(_entityCapacity, _archetypes, _arenas);
                checksum += plan.RequiredChunkCount + plan.TotalReservedBytes;
            }

            return new ScenarioMeasurement(_operations, checksum);
        }
    }

    private sealed class NativeArenaLifecycleFixture : IDisposable
    {
        private readonly NativeChunkArena _arena;
        private readonly int _operations;
        private ChunkHandle _handle;

        internal NativeArenaLifecycleFixture(ChunkLayoutOptions options, int operations)
        {
            _arena = new NativeChunkArena(new ChunkArenaCapacity(
                options.ChunkSizeBytes,
                options.MinimumColumnAlignmentBytes,
                maximumChunks: 1));
            _handle = _arena.Acquire();
            _operations = operations;
        }

        internal ScenarioMeasurement Execute()
        {
            ulong checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                if (!_arena.TryRelease(_handle) || !_arena.TryAcquire(out _handle))
                    throw new InvalidOperationException("Native lifecycle fixture lost its only chunk.");
                checksum += _handle.Generation;
            }

            return new ScenarioMeasurement(_operations, unchecked((long)checksum));
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class PinnedArenaLifecycleFixture : IDisposable
    {
        private readonly PinnedChunkArena _arena;
        private readonly int _operations;
        private ChunkHandle _handle;

        internal PinnedArenaLifecycleFixture(ChunkLayoutOptions options, int operations)
        {
            _arena = new PinnedChunkArena(new ChunkArenaCapacity(
                options.ChunkSizeBytes,
                options.MinimumColumnAlignmentBytes,
                maximumChunks: 1));
            _handle = _arena.Acquire();
            _operations = operations;
        }

        internal ScenarioMeasurement Execute()
        {
            ulong checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                if (!_arena.TryRelease(_handle) || !_arena.TryAcquire(out _handle))
                    throw new InvalidOperationException("Pinned lifecycle fixture lost its only chunk.");
                checksum += _handle.Generation;
            }

            return new ScenarioMeasurement(_operations, unchecked((long)checksum));
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class NativeArenaTraversalFixture : IDisposable
    {
        private readonly NativeChunkArena _arena;
        private readonly ChunkLayoutPlan _layout;
        private readonly ComponentColumnLayout _positionColumn;
        private readonly ChunkHandle[] _chunks;
        private readonly int[] _rowCounts;
        private readonly int _population;

        internal NativeArenaTraversalFixture(int population)
        {
            LayoutReportBuilder.CreateNormalLayout(
                ChunkLayoutOptions.KiB32,
                out _layout,
                out ComponentColumnLayout[] columns);
            _positionColumn = LayoutReportBuilder.FindColumn<BenchmarkPosition>(columns);
            var capacityPlan = new ArchetypeCapacityPlan(_layout, population);
            _arena = new NativeChunkArena(ChunkArenaCapacity.FromLayout(_layout, capacityPlan.RequiredChunkCount));
            _chunks = new ChunkHandle[capacityPlan.RequiredChunkCount];
            _rowCounts = new int[_chunks.Length];
            _population = population;
            Initialize();
        }

        private void Initialize()
        {
            int remaining = _population;
            int ordinal = 0;
            for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
            {
                ChunkHandle chunk = _arena.Acquire();
                int rows = Math.Min(remaining, _layout.RowCapacity);
                Span<BenchmarkPosition> positions = _arena.GetColumn<BenchmarkPosition>(chunk, _layout, _positionColumn);
                for (int row = 0; row < rows; row++, ordinal++)
                    positions[row] = new BenchmarkPosition { X = ordinal, Y = ordinal * 0.5f };
                _chunks[chunkIndex] = chunk;
                _rowCounts[chunkIndex] = rows;
                remaining -= rows;
            }
        }

        internal ScenarioMeasurement Execute()
        {
            double checksum = 0;
            for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
            {
                Span<BenchmarkPosition> positions = _arena
                    .GetColumn<BenchmarkPosition>(_chunks[chunkIndex], _layout, _positionColumn)
                    [.._rowCounts[chunkIndex]];
                for (int row = 0; row < positions.Length; row++)
                    checksum += positions[row].X + positions[row].Y;
            }

            return new ScenarioMeasurement(_population, BitConverter.DoubleToInt64Bits(checksum));
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class PinnedArenaTraversalFixture : IDisposable
    {
        private readonly PinnedChunkArena _arena;
        private readonly ChunkLayoutPlan _layout;
        private readonly ComponentColumnLayout _positionColumn;
        private readonly ChunkHandle[] _chunks;
        private readonly int[] _rowCounts;
        private readonly int _population;

        internal PinnedArenaTraversalFixture(int population)
        {
            LayoutReportBuilder.CreateNormalLayout(
                ChunkLayoutOptions.KiB32,
                out _layout,
                out ComponentColumnLayout[] columns);
            _positionColumn = LayoutReportBuilder.FindColumn<BenchmarkPosition>(columns);
            var capacityPlan = new ArchetypeCapacityPlan(_layout, population);
            _arena = new PinnedChunkArena(ChunkArenaCapacity.FromLayout(_layout, capacityPlan.RequiredChunkCount));
            _chunks = new ChunkHandle[capacityPlan.RequiredChunkCount];
            _rowCounts = new int[_chunks.Length];
            _population = population;
            Initialize();
        }

        private void Initialize()
        {
            int remaining = _population;
            int ordinal = 0;
            for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
            {
                ChunkHandle chunk = _arena.Acquire();
                int rows = Math.Min(remaining, _layout.RowCapacity);
                Span<BenchmarkPosition> positions = _arena.GetColumn<BenchmarkPosition>(chunk, _layout, _positionColumn);
                for (int row = 0; row < rows; row++, ordinal++)
                    positions[row] = new BenchmarkPosition { X = ordinal, Y = ordinal * 0.5f };
                _chunks[chunkIndex] = chunk;
                _rowCounts[chunkIndex] = rows;
                remaining -= rows;
            }
        }

        internal ScenarioMeasurement Execute()
        {
            double checksum = 0;
            for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
            {
                Span<BenchmarkPosition> positions = _arena
                    .GetColumn<BenchmarkPosition>(_chunks[chunkIndex], _layout, _positionColumn)
                    [.._rowCounts[chunkIndex]];
                for (int row = 0; row < positions.Length; row++)
                    checksum += positions[row].X + positions[row].Y;
            }

            return new ScenarioMeasurement(_population, BitConverter.DoubleToInt64Bits(checksum));
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class NativeArenaClearFixture : IDisposable
    {
        private readonly NativeChunkArena _arena;
        private readonly ChunkLayoutPlan _layout;
        private readonly ComponentColumnLayout _positionColumn;
        private readonly ChunkHandle _chunk;
        private readonly int _wholeChunkOperations;
        private readonly int _activeRowOperations;
        private readonly int _activeRows;

        internal NativeArenaClearFixture(int wholeChunkOperations, int activeRowOperations)
        {
            LayoutReportBuilder.CreateNormalLayout(ChunkLayoutOptions.KiB32, out _layout, out ComponentColumnLayout[] columns);
            _positionColumn = LayoutReportBuilder.FindColumn<BenchmarkPosition>(columns);
            _arena = new NativeChunkArena(ChunkArenaCapacity.FromLayout(_layout, maximumChunks: 1));
            _chunk = _arena.Acquire();
            _wholeChunkOperations = wholeChunkOperations;
            _activeRowOperations = activeRowOperations;
            _activeRows = Math.Max(1, _layout.RowCapacity / 10);
        }

        internal ScenarioMeasurement ExecuteWholeChunk()
        {
            for (int i = 0; i < _wholeChunkOperations; i++)
                _arena.ClearChunk(_chunk);
            return new ScenarioMeasurement(_wholeChunkOperations, _layout.BytesUsed);
        }

        internal ScenarioMeasurement ExecuteActiveRows()
        {
            Span<BenchmarkPosition> active = _arena
                .GetColumn<BenchmarkPosition>(_chunk, _layout, _positionColumn)
                [.._activeRows];
            for (int i = 0; i < _activeRowOperations; i++)
                active.Clear();
            return new ScenarioMeasurement(_activeRowOperations, _activeRows);
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class PinnedArenaClearFixture : IDisposable
    {
        private readonly PinnedChunkArena _arena;
        private readonly ChunkLayoutPlan _layout;
        private readonly ComponentColumnLayout _positionColumn;
        private readonly ChunkHandle _chunk;
        private readonly int _wholeChunkOperations;
        private readonly int _activeRowOperations;
        private readonly int _activeRows;

        internal PinnedArenaClearFixture(int wholeChunkOperations, int activeRowOperations)
        {
            LayoutReportBuilder.CreateNormalLayout(ChunkLayoutOptions.KiB32, out _layout, out ComponentColumnLayout[] columns);
            _positionColumn = LayoutReportBuilder.FindColumn<BenchmarkPosition>(columns);
            _arena = new PinnedChunkArena(ChunkArenaCapacity.FromLayout(_layout, maximumChunks: 1));
            _chunk = _arena.Acquire();
            _wholeChunkOperations = wholeChunkOperations;
            _activeRowOperations = activeRowOperations;
            _activeRows = Math.Max(1, _layout.RowCapacity / 10);
        }

        internal ScenarioMeasurement ExecuteWholeChunk()
        {
            for (int i = 0; i < _wholeChunkOperations; i++)
                _arena.ClearChunk(_chunk);
            return new ScenarioMeasurement(_wholeChunkOperations, _layout.BytesUsed);
        }

        internal ScenarioMeasurement ExecuteActiveRows()
        {
            Span<BenchmarkPosition> active = _arena
                .GetColumn<BenchmarkPosition>(_chunk, _layout, _positionColumn)
                [.._activeRows];
            for (int i = 0; i < _activeRowOperations; i++)
                active.Clear();
            return new ScenarioMeasurement(_activeRowOperations, _activeRows);
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class ArchetypeBulkAddFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly ArchetypeChunkStore<TArena> _store;
        private readonly uint[] _entityIndices;
        private readonly ChunkRow[] _locations;

        internal ArchetypeBulkAddFixture(int population)
        {
            _store = ArchetypeBenchmarkFactory.CreateStore<TArena>(population, out _, out _);
            _entityIndices = new uint[population];
            _locations = new ChunkRow[population];
            for (int i = 0; i < population; i++)
                _entityIndices[i] = (uint)i;
        }

        internal void Prepare()
        {
            for (int i = _store.Count - 1; i >= 0; i--)
            {
                if (!_store.TryRemove(_locations[i], _entityIndices[i], out ChunkRowRelocation relocation) || relocation.HasValue)
                    throw new InvalidOperationException("Descending bulk fixture removal violated dense insertion order.");
            }
        }

        internal ScenarioMeasurement Execute()
        {
            int added = _store.Add(_entityIndices, _locations);
            long checksum = _store.Count + _locations[0].Row + _locations[^1].Row;
            return new ScenarioMeasurement(added, checksum);
        }

        public void Dispose() => _store.Dispose();
    }

    private sealed class ArchetypeSwapChurnFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly ArchetypeChunkStore<TArena> _store;
        private readonly ChunkRow[] _locations;
        private readonly int _cycles;

        internal ArchetypeSwapChurnFixture(int population, int cycles)
        {
            _store = ArchetypeBenchmarkFactory.CreateStore<TArena>(population, out _, out _);
            _locations = new ChunkRow[population];
            var entityIndices = new uint[population];
            for (int i = 0; i < population; i++)
                entityIndices[i] = (uint)i;
            if (_store.Add(entityIndices, _locations) != population)
                throw new InvalidOperationException("Swap-churn fixture initialization exhausted its store.");
            _cycles = cycles;
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int cycle = 0; cycle < _cycles; cycle++)
            {
                uint entityIndex = (uint)cycle % (uint)_locations.Length;
                if (!_store.TryRemove(_locations[entityIndex], entityIndex, out ChunkRowRelocation relocation))
                    throw new InvalidOperationException("Swap-churn fixture lost a live entity.");
                if (relocation.HasValue)
                    _locations[relocation.EntityIndex] = relocation.Destination;
                if (!_store.TryAdd(entityIndex, out _locations[entityIndex]))
                    throw new InvalidOperationException("Swap-churn fixture failed to restore capacity.");
                checksum += _locations[entityIndex].Row + (relocation.HasValue ? 1 : 0);
            }

            return new ScenarioMeasurement(_cycles, checksum);
        }

        public void Dispose() => _store.Dispose();
    }

    private sealed class ArchetypeEnableToggleFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly ArchetypeChunkStore<TArena> _store;
        private readonly int _enableColumnIndex;
        private readonly ChunkRow[] _locations;
        private readonly int _operations;

        internal ArchetypeEnableToggleFixture(int population, int operations)
        {
            _store = ArchetypeBenchmarkFactory.CreateStore<TArena>(population, out _, out ComponentColumnLayout[] columns);
            ComponentColumnLayout enableColumn = LayoutReportBuilder.FindColumn<BenchmarkVelocity>(columns);
            _enableColumnIndex = _store.GetColumnIndex(enableColumn.ComponentId);
            _locations = new ChunkRow[population];
            var entityIndices = new uint[population];
            for (int i = 0; i < population; i++)
                entityIndices[i] = (uint)i;
            if (_store.Add(entityIndices, _locations) != population)
                throw new InvalidOperationException("Enable-toggle fixture initialization exhausted its store.");
            _operations = operations;
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                ChunkRow location = _locations[i % _locations.Length];
                bool enabled = (i & 1) == 0;
                _store.SetEnabled(location, _enableColumnIndex, enabled);
                checksum += enabled ? 1 : 0;
            }

            return new ScenarioMeasurement(_operations, checksum);
        }

        public void Dispose() => _store.Dispose();
    }

    private sealed class ArchetypeMigrationFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly ArchetypeRegistry<TArena> _registry;
        private readonly EntityPool _pool;
        private readonly Entity _entity;
        private readonly ArchetypeTransitionPlan<TArena> _forward;
        private readonly ArchetypeTransitionPlan<TArena> _backward;
        private readonly ComponentColumnLayout _sourceVelocity;
        private readonly int _operations;

        internal ArchetypeMigrationFixture(int operations)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _registry = new ArchetypeRegistry<TArena>(
                schema,
                maximumArchetypes: 2,
                static capacity => CreateArena(capacity));
            ComponentId disabled = BenchmarkSchema.GetComponentId<BenchmarkDisabled>();
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ComponentId[] sourceSignature = CreateSortedSignature(position, velocity);
            ComponentId[] destinationSignature = CreateSortedSignature(disabled, velocity, wide);
            ArchetypeId sourceId = _registry.Register(sourceSignature, ChunkLayoutOptions.KiB16, 1);
            ArchetypeId destinationId = _registry.Register(destinationSignature, ChunkLayoutOptions.KiB16, 1);
            ArchetypeStorage<TArena> source = _registry.GetStorage(sourceId);
            _sourceVelocity = FindColumn(source.Columns, velocity);
            _forward = _registry.PrepareTransition(sourceId, destinationId);
            _backward = _registry.PrepareTransition(destinationId, sourceId);
            _registry.Seal();

            _pool = new EntityPool(WorldId.Create(), new EntityCapacity(1));
            _entity = _pool.Allocate();
            if (!source.Store.TryAdd(_entity.Index, out ChunkRow row) ||
                !_pool.TryBindLocation(_entity, new EntityLocation(sourceId, row)))
            {
                throw new InvalidOperationException("Migration benchmark fixture initialization failed.");
            }
            source.Store.GetComponentRef<BenchmarkVelocity>(row, _sourceVelocity) =
                new BenchmarkVelocity { X = 123.5f, Y = -77.25f };
            source.Store.SetEnabled(row, _sourceVelocity, enabled: false);
            _operations = operations;
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                ArchetypeTransitionPlan<TArena> plan = (i & 1) == 0 ? _forward : _backward;
                if (!ArchetypeMigrator.TryMigrate(_pool, _entity, plan, out EntityLocation location))
                    throw new InvalidOperationException("Migration benchmark lost its entity.");
                checksum += location.ArchetypeId.Value + (uint)location.ChunkRow.Row;
            }

            if (!_pool.TryGetLocation(_entity, out EntityLocation finalLocation) ||
                finalLocation.ArchetypeId != _backward.Destination.Id)
            {
                throw new InvalidOperationException("Migration benchmark ended in the wrong archetype.");
            }
            BenchmarkVelocity retained = _backward.Destination.Store
                .GetComponentRef<BenchmarkVelocity>(finalLocation.ChunkRow, _sourceVelocity);
            checksum += BitConverter.SingleToInt32Bits(retained.X);
            checksum += BitConverter.SingleToInt32Bits(retained.Y);
            checksum += _backward.Destination.Store.IsEnabled(finalLocation.ChunkRow, _sourceVelocity) ? 1 : 0;
            return new ScenarioMeasurement(_operations, checksum);
        }

        public void Dispose() => _registry.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException($"Unsupported migration benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }

        private static ComponentId[] CreateSortedSignature(params ComponentId[] signature)
        {
            Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));
            return signature;
        }

        private static ComponentColumnLayout FindColumn(
            ReadOnlySpan<ComponentColumnLayout> columns,
            ComponentId componentId)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                if (columns[i].ComponentId == componentId)
                    return columns[i];
            }

            throw new InvalidOperationException($"Migration benchmark column {componentId} was not found.");
        }
    }

    private sealed class EcsWorldLifecycleFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeId _sourceId;
        private readonly ArchetypeTransitionPlan<TArena> _forward;
        private readonly int _operations;

        internal EcsWorldLifecycleFixture(int operations)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(1),
                maximumArchetypes: 2,
                static capacity => CreateArena(capacity),
                maximumQueryPlans: 0);
            ComponentId disabled = BenchmarkSchema.GetComponentId<BenchmarkDisabled>();
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ComponentId[] sourceSignature = CreateSortedSignature(position, velocity);
            ComponentId[] destinationSignature = CreateSortedSignature(disabled, velocity, wide);
            _sourceId = _world.RegisterArchetype(
                sourceSignature,
                ChunkLayoutOptions.KiB16,
                maximumChunks: 1);
            ArchetypeId destinationId = _world.RegisterArchetype(
                destinationSignature,
                ChunkLayoutOptions.KiB16,
                maximumChunks: 1);
            _forward = _world.PrepareTransition(_sourceId, destinationId);
            _world.Seal();
            _operations = operations;
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                if (!_world.TryCreateDefault(_sourceId, out Entity entity))
                    throw new InvalidOperationException("World lifecycle benchmark failed to create an entity.");
                if (!_world.TryTransition(entity, _forward, out EntityLocation destination))
                    throw new InvalidOperationException("World lifecycle benchmark failed to transition an entity.");
                checksum = unchecked(checksum + entity.Generation + destination.ArchetypeId.Value);
                if (!_world.TryDestroy(entity))
                    throw new InvalidOperationException("World lifecycle benchmark failed to destroy an entity.");
            }

            if (_world.AliveCount != 0 || _world.LocatedCount != 0)
                throw new InvalidOperationException("World lifecycle benchmark leaked a live entity.");
            return new ScenarioMeasurement(_operations, checksum);
        }

        public void Dispose() => _world.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported world lifecycle benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }

        private static ComponentId[] CreateSortedSignature(params ComponentId[] signature)
        {
            Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));
            return signature;
        }
    }

    private sealed class EcsWorldBoundAccessFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeComponentBinding<BenchmarkVelocity> _binding;
        private readonly Entity _entity;
        private readonly int _operations;

        internal EcsWorldBoundAccessFixture(int operations)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(1),
                maximumArchetypes: 1,
                static capacity => CreateArena(capacity),
                maximumQueryPlans: 0);
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ArchetypeId archetype = _world.RegisterArchetype(
                [velocity],
                ChunkLayoutOptions.KiB16,
                maximumChunks: 1);
            _binding = _world.BindComponent<BenchmarkVelocity>(archetype, velocity);
            _world.Seal();
            if (!_world.TryCreateDefault(archetype, out _entity))
                throw new InvalidOperationException("Bound-access benchmark fixture failed to create its entity.");
            _operations = operations;
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                var value = new BenchmarkVelocity { X = i, Y = -i };
                if (!_world.TrySetComponent(_entity, _binding, value) ||
                    !_world.TryGetComponent(_entity, _binding, out BenchmarkVelocity read) ||
                    !_world.TrySetComponentEnabled(_entity, _binding, (i & 1) == 0) ||
                    !_world.TryGetComponentEnabled(_entity, _binding, out bool enabled))
                {
                    throw new InvalidOperationException("Bound-access benchmark lost its entity or binding.");
                }

                checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(read.X));
                checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(read.Y));
                checksum += enabled ? 1 : 0;
            }

            return new ScenarioMeasurement(_operations, checksum);
        }

        public void Dispose() => _world.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported bound-access benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }
    }

    private sealed class PublicEcsWorldLookupFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly ComponentLookup<BenchmarkVelocity> _lookup;
        private readonly ArchetypeTransition _forward;
        private readonly ArchetypeTransition _backward;
        private readonly Entity _entity;
        private readonly int _operations;
        private readonly bool _includeTransition;

        internal PublicEcsWorldLookupFixture(int operations, bool includeTransition)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld(
                schema,
                new EcsWorldOptions(
                    new EntityCapacity(1),
                    maximumArchetypes: includeTransition ? 2 : 1,
                    maximumPreparedPlans: 1,
                    maximumPreparedTransitions: includeTransition ? 2 : 0));
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ArchetypeHandle source = _world.RegisterArchetype(
                CreateSortedSignature(position, velocity),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 1);
            if (includeTransition)
            {
                ArchetypeHandle destination = _world.RegisterArchetype(
                    CreateSortedSignature(velocity, wide),
                    ChunkLayoutOptions.KiB16,
                    maximumChunks: 1);
                _forward = _world.PrepareTransition(source, destination);
                _backward = _world.PrepareTransition(destination, source);
            }

            _lookup = _world.PrepareLookup<BenchmarkVelocity>();
            _world.Seal();
            _world.Initialize();
            _world.BeginRun();
            if (!_world.TryCreate(source, out _entity))
                throw new InvalidOperationException("Public lookup benchmark fixture failed to create its entity.");

            _operations = operations;
            _includeTransition = includeTransition;
        }

        internal long RouteBytes => _lookup.RouteBytes;

        internal ScenarioMeasurement ExecutePrepared()
        {
            var job = new PreparedAccessJob(_entity);
            if (!_includeTransition)
            {
                job.OperationCount = _operations;
                _lookup.Execute(_world, ref job);
                return new ScenarioMeasurement(_operations, job.Checksum);
            }

            for (int i = 0; i < _operations; i++)
            {
                job.FirstOperation = i;
                job.OperationCount = 1;
                _world.ExecuteAccess(_lookup, ref job);
                if (!_world.TryTransition(
                    _entity,
                    (i & 1) == 0 ? _forward : _backward))
                {
                    throw new InvalidOperationException("Public bounded access-job transition failed.");
                }
            }

            return new ScenarioMeasurement(_operations, job.Checksum);
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                var value = new BenchmarkVelocity { X = i, Y = -i };
                if (!_world.TryWrite(_entity, _lookup, value) ||
                    !_world.TryRead(_entity, _lookup, out BenchmarkVelocity read) ||
                    !_world.TrySetEnabled(_entity, _lookup, (i & 1) == 0) ||
                    !_world.TryGetEnabled(_entity, _lookup, out bool enabled))
                {
                    throw new InvalidOperationException("Public prepared lookup lost its entity route.");
                }

                checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(read.X));
                checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(read.Y));
                checksum += enabled ? 1 : 0;
                if (_includeTransition && !_world.TryTransition(
                    _entity,
                    (i & 1) == 0 ? _forward : _backward))
                {
                    throw new InvalidOperationException("Public prepared lookup transition failed.");
                }
            }

            return new ScenarioMeasurement(_operations, checksum);
        }

        public void Dispose() => _world.Dispose();

        private struct PreparedAccessJob : IComponentAccessJob<BenchmarkVelocity>
        {
            private readonly Entity _entity;

            internal PreparedAccessJob(Entity entity)
            {
                _entity = entity;
                FirstOperation = 0;
                OperationCount = 0;
                Checksum = 0;
            }

            internal int FirstOperation;
            internal int OperationCount;
            internal long Checksum;

            public void Execute(in ComponentAccessor<BenchmarkVelocity> access)
            {
                int end = checked(FirstOperation + OperationCount);
                long checksum = Checksum;
                for (int i = FirstOperation; i < end; i++)
                {
                    var value = new BenchmarkVelocity { X = i, Y = -i };
                    if (!access.TryWrite(_entity, value) ||
                        !access.TryRead(_entity, out BenchmarkVelocity read) ||
                        !access.TrySetEnabled(_entity, (i & 1) == 0) ||
                        !access.TryGetEnabled(_entity, out bool enabled))
                    {
                        throw new InvalidOperationException("Public bounded access job lost its entity route.");
                    }

                    checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(read.X));
                    checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(read.Y));
                    checksum += enabled ? 1 : 0;
                }

                Checksum = checksum;
            }
        }

        private static ComponentId[] CreateSortedSignature(params ComponentId[] signature)
        {
            Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));
            return signature;
        }
    }

    private sealed class PublicDeferredTransitionFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly StructuralCommandBuffer _commands;
        private readonly ArchetypeTransition _forward;
        private readonly ArchetypeTransition _backward;
        private readonly Entity[] _entities;
        private readonly Entity[] _resetEntities;
        private readonly int _timedCycleCount;
        private bool _inDestination;

        internal PublicDeferredTransitionFixture(
            int entityCount,
            bool retainWide = false,
            bool scatteredSource = false,
            int residentEntityCount = 0,
            int timedCycleCount = 0,
            bool packedSuffix = false,
            bool trackExactRows = false)
        {
            residentEntityCount = residentEntityCount == 0 ? entityCount : residentEntityCount;
            if (residentEntityCount < entityCount)
                throw new ArgumentOutOfRangeException(nameof(residentEntityCount));
            if (timedCycleCount < 0)
                throw new ArgumentOutOfRangeException(nameof(timedCycleCount));
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld(
                schema,
                new EcsWorldOptions(
                    new EntityCapacity(residentEntityCount),
                    maximumArchetypes: 2,
                    maximumPreparedPlans: trackExactRows ? 1 : 0,
                    maximumPreparedTransitions: 2,
                    maximumStructuralCommands: entityCount));
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ComponentId scalar = BenchmarkSchema.GetComponentId<BenchmarkScalar>();
            ArchetypeHandle source = retainWide
                ? _world.RegisterArchetype(
                    CreateSortedSignature(position, velocity, wide),
                    ChunkLayoutOptions.KiB16,
                    maximumChunks: 640)
                : _world.RegisterArchetype(
                    CreateSortedSignature(position, velocity),
                    ChunkLayoutOptions.KiB16,
                    maximumChunks: 640);
            ArchetypeHandle destination = retainWide
                ? _world.RegisterArchetype(
                    CreateSortedSignature(position, velocity, wide, scalar),
                    ChunkLayoutOptions.KiB16,
                    maximumChunks: 640)
                : _world.RegisterArchetype(
                    CreateSortedSignature(position, velocity, wide),
                    ChunkLayoutOptions.KiB16,
                    maximumChunks: 640);
            _forward = _world.PrepareTransition(source, destination);
            _backward = _world.PrepareTransition(destination, source);
            if (trackExactRows)
            {
                if (!retainWide)
                {
                    throw new ArgumentException(
                        "Exact-row migration measurement requires a retained wide component.",
                        nameof(trackExactRows));
                }
                FieldInfo coreField = typeof(EcsWorld).GetField(
                    "_core",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException(
                        "Benchmark public-world core field was not found.");
                var core = (EcsWorld<NativeChunkArena>)(coreField.GetValue(_world)
                    ?? throw new InvalidOperationException("Benchmark public-world core was null."));
                _ = core.PrepareChangedRowsQuery<BenchmarkWide>(wide, wide);
            }
            _world.Seal();
            _world.Initialize();
            _world.BeginRun();
            _commands = _world.StructuralCommands;
            _entities = new Entity[entityCount];
            int targetStart = packedSuffix ? residentEntityCount - entityCount : 0;
            for (int index = 0; index < residentEntityCount; index++)
            {
                bool isTarget = index >= targetStart && index < targetStart + entityCount;
                if (!_world.TryCreate(source, out Entity entity))
                    throw new InvalidOperationException("Deferred-transition fixture exhausted its source.");
                if (isTarget)
                    _entities[index - targetStart] = entity;
            }
            _resetEntities = scatteredSource
                ? CreateInterleavedOrder(_entities)
                : _entities;
            _timedCycleCount = timedCycleCount;
        }

        internal long CommandReservedBytes => _commands.ReservedBytes;

        internal StructuralPlaybackDiagnostics LastDiagnostics =>
            _commands.LastPlaybackDiagnostics;

        internal void Prepare()
        {
            if (!_inDestination)
                return;
            _commands.EnqueueTransition(_resetEntities, _backward);
            StructuralPlaybackResult result = _world.PlaybackStructuralCommands();
            if (result.RemovedCount != _entities.Length || result.RejectedCount != 0)
                throw new InvalidOperationException("Deferred-transition fixture failed to reset.");
            _inDestination = false;
        }

        internal ScenarioMeasurement Execute()
        {
            _commands.EnqueueTransition(_entities, _forward);
            StructuralPlaybackResult result = _world.PlaybackStructuralCommands();
            if (result.AddedCount != _entities.Length || result.RejectedCount != 0)
                throw new InvalidOperationException("Deferred-transition batch was not applied exactly once.");
            _inDestination = true;
            long checksum = unchecked(
                (long)result.Epoch + result.CommandCount + result.AddedCount);
            return new ScenarioMeasurement(
                _entities.Length,
                checksum,
                _commands.LastPlaybackDiagnostics);
        }

        internal ScenarioMeasurement ExecuteScalarOracle()
        {
            _commands.EnqueueTransition(_entities, _forward);
            StructuralPlaybackResult result =
                _world.PlaybackStructuralCommandsScalarOracle();
            if (result.AddedCount != _entities.Length || result.RejectedCount != 0)
                throw new InvalidOperationException("Scalar-oracle transition batch was not applied exactly once.");
            _inDestination = true;
            long checksum = unchecked(
                (long)result.Epoch + result.CommandCount + result.AddedCount);
            return new ScenarioMeasurement(
                _entities.Length,
                checksum,
                _commands.LastPlaybackDiagnostics);
        }

        internal void PrepareRecorded()
        {
            Prepare();
            _commands.EnqueueTransition(_entities, _forward);
        }

        internal ScenarioMeasurement ExecuteRecorded()
        {
            StructuralPlaybackResult result = _world.PlaybackStructuralCommands();
            return CompleteRecorded(result, "Grouped");
        }

        internal ScenarioMeasurement ExecuteRecordedScalarOracle()
        {
            StructuralPlaybackResult result =
                _world.PlaybackStructuralCommandsScalarOracle();
            return CompleteRecorded(result, "Scalar-oracle");
        }

        internal ScenarioMeasurement ExecuteRoundTrips() =>
            ExecuteRoundTripsCore(useScalarOracle: false);

        internal ScenarioMeasurement ExecuteRoundTripsScalarOracle() =>
            ExecuteRoundTripsCore(useScalarOracle: true);

        private ScenarioMeasurement ExecuteRoundTripsCore(bool useScalarOracle)
        {
            if (_timedCycleCount <= 0 || _inDestination)
                throw new InvalidOperationException("Deferred-transition round trips require a configured source state.");

            long checksum = 0;
            for (int cycle = 0; cycle < _timedCycleCount; cycle++)
            {
                _commands.EnqueueTransition(_entities, _forward);
                StructuralPlaybackResult forward = useScalarOracle
                    ? _world.PlaybackStructuralCommandsScalarOracle()
                    : _world.PlaybackStructuralCommands();
                if (forward.AddedCount != _entities.Length || forward.RejectedCount != 0)
                    throw new InvalidOperationException("Deferred-transition round-trip forward phase failed.");
                _inDestination = true;

                _commands.EnqueueTransition(_resetEntities, _backward);
                StructuralPlaybackResult backward = useScalarOracle
                    ? _world.PlaybackStructuralCommandsScalarOracle()
                    : _world.PlaybackStructuralCommands();
                if (backward.RemovedCount != _entities.Length || backward.RejectedCount != 0)
                    throw new InvalidOperationException("Deferred-transition round-trip backward phase failed.");
                _inDestination = false;
                checksum = unchecked(
                    checksum + forward.Epoch + backward.Epoch +
                    forward.AddedCount + backward.RemovedCount);
            }

            return new ScenarioMeasurement(
                checked(_entities.Length * _timedCycleCount * 2),
                checksum);
        }

        private ScenarioMeasurement CompleteRecorded(
            in StructuralPlaybackResult result,
            string executor)
        {
            if (result.AddedCount != _entities.Length || result.RejectedCount != 0)
                throw new InvalidOperationException($"{executor} recorded transition batch was not applied exactly once.");
            _inDestination = true;
            long checksum = unchecked(
                (long)result.Epoch + result.CommandCount + result.AddedCount);
            return new ScenarioMeasurement(
                _entities.Length,
                checksum,
                _commands.LastPlaybackDiagnostics);
        }

        public void Dispose() => _world.Dispose();

        private static ComponentId[] CreateSortedSignature(params ComponentId[] signature)
        {
            Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));
            return signature;
        }

        private static Entity[] CreateInterleavedOrder(Entity[] entities)
        {
            var interleaved = new Entity[entities.Length];
            int lowerCount = (entities.Length + 1) >> 1;
            for (int index = 0; index < interleaved.Length; index++)
            {
                int sourceIndex = (index & 1) == 0
                    ? index >> 1
                    : lowerCount + (index >> 1);
                interleaved[index] = entities[sourceIndex];
            }
            return interleaved;
        }
    }

    private sealed class PublicDeferredCreateFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly StructuralCommandBuffer _commands;
        private readonly ArchetypeHandle[] _archetypes;
        private readonly DeferredEntity[] _tokens;
        private readonly Entity[] _entities;
        private bool _hasLiveEntities;

        internal PublicDeferredCreateFixture(int entityCount)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld(
                schema,
                new EcsWorldOptions(
                    new EntityCapacity(entityCount),
                    maximumArchetypes: 1,
                    maximumPreparedPlans: 0,
                    maximumPreparedTransitions: 0,
                    maximumStructuralCommands: entityCount));
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ArchetypeHandle archetype = _world.RegisterArchetype(
                [position],
                ChunkLayoutOptions.KiB16,
                maximumChunks: 128);
            _world.Seal();
            _world.Initialize();
            _world.BeginRun();
            _commands = _world.StructuralCommands;
            _archetypes = new ArchetypeHandle[entityCount];
            _tokens = new DeferredEntity[entityCount];
            _entities = new Entity[entityCount];
            Array.Fill(_archetypes, archetype);
        }

        internal void Prepare()
        {
            if (!_hasLiveEntities)
                return;
            for (int index = 0; index < _entities.Length; index++)
            {
                if (!_world.TryDestroy(_entities[index]))
                    throw new InvalidOperationException("Deferred-create fixture failed to reset.");
            }
            _hasLiveEntities = false;
        }

        internal ScenarioMeasurement Execute()
        {
            _commands.EnqueueCreateDefault(_archetypes, _tokens);
            StructuralPlaybackResult result = _world.PlaybackStructuralCommands();
            if (result.CreatedCount != _entities.Length || result.RejectedCount != 0)
                throw new InvalidOperationException("Deferred-create batch was not applied exactly once.");

            long checksum = result.Epoch;
            for (int index = 0; index < _tokens.Length; index++)
            {
                if (!_commands.TryResolve(_tokens[index], out _entities[index]))
                    throw new InvalidOperationException("Deferred-create token did not resolve.");
                checksum = unchecked(checksum + _entities[index].Index + _entities[index].Generation);
            }
            _hasLiveEntities = true;
            return new ScenarioMeasurement(_entities.Length, checksum);
        }

        public void Dispose() => _world.Dispose();
    }

    private enum DeferredDestroySelection
    {
        Whole,
        Tail1Percent,
        Tail10Percent,
        Tail50Percent,
        Sparse1Percent,
        Sparse10Percent,
        Sparse50Percent,
    }

    private sealed class PublicDeferredDestroyFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly StructuralCommandBuffer _commands;
        private readonly ArchetypeHandle _archetype;
        private readonly Entity[] _entities;
        private readonly Entity[] _targets;
        private readonly DeferredDestroySelection _selection;
        private readonly int _targetOffset;
        private bool _hasLiveTargets;

        internal PublicDeferredDestroyFixture(
            int entityCount,
            DeferredDestroySelection selection = DeferredDestroySelection.Whole)
        {
            int targetCount = selection switch
            {
                DeferredDestroySelection.Whole => entityCount,
                DeferredDestroySelection.Tail1Percent or
                DeferredDestroySelection.Sparse1Percent => entityCount / 100,
                DeferredDestroySelection.Tail10Percent or
                DeferredDestroySelection.Sparse10Percent => entityCount / 10,
                DeferredDestroySelection.Tail50Percent or
                DeferredDestroySelection.Sparse50Percent => entityCount / 2,
                _ => throw new ArgumentOutOfRangeException(nameof(selection)),
            };
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld(
                schema,
                new EcsWorldOptions(
                    new EntityCapacity(entityCount),
                    maximumArchetypes: 1,
                    maximumPreparedPlans: 0,
                    maximumPreparedTransitions: 0,
                    maximumStructuralCommands: targetCount));
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            _archetype = _world.RegisterArchetype(
                [position],
                ChunkLayoutOptions.KiB16,
                maximumChunks: 128);
            _world.Seal();
            _world.Initialize();
            _world.BeginRun();
            _commands = _world.StructuralCommands;
            _entities = new Entity[entityCount];
            _selection = selection;
            _targetOffset = IsTailSelection(selection)
                ? entityCount - targetCount
                : 0;
            _targets = selection == DeferredDestroySelection.Whole
                ? _entities
                : new Entity[targetCount];
            CreateInitialEntities();
        }

        internal void Prepare()
        {
            if (_hasLiveTargets)
                return;

            if (IsSparseSelection(_selection))
            {
                // Sparse swap removal changes packed ownership. Rebuild the
                // complete population outside timing so every measured sample
                // again targets the requested ordinal physical stride.
                for (int index = 0; index < _entities.Length; index++)
                {
                    if (_world.IsAlive(_entities[index]) &&
                        !_world.TryDestroy(_entities[index]))
                    {
                        throw new InvalidOperationException(
                            "Sparse destroy fixture could not clear a survivor.");
                    }
                }
                for (int index = 0; index < _entities.Length; index++)
                {
                    if (!_world.TryCreate(_archetype, out _entities[index]))
                    {
                        throw new InvalidOperationException(
                            "Sparse destroy fixture could not rebuild its packed population.");
                    }
                }
                FillTargets();
                _hasLiveTargets = true;
                return;
            }

            for (int index = 0; index < _targets.Length; index++)
            {
                if (!_world.TryCreate(_archetype, out Entity entity))
                    throw new InvalidOperationException("Deferred-destroy fixture failed to restore a target.");
                _targets[index] = entity;
                _entities[_targetOffset + index] = entity;
            }
            _hasLiveTargets = true;
        }

        internal ScenarioMeasurement Execute()
        {
            _commands.EnqueueDestroy(_targets);
            StructuralPlaybackResult result = _world.PlaybackStructuralCommands();
            return Complete(result, "Grouped end-to-end");
        }

        internal void PrepareRecorded()
        {
            Prepare();
            _commands.EnqueueDestroy(_targets);
        }

        internal ScenarioMeasurement ExecuteRecorded() =>
            Complete(_world.PlaybackStructuralCommands(), "Grouped playback");

        internal ScenarioMeasurement ExecuteRecordedScalarOracle() =>
            Complete(
                _world.PlaybackStructuralCommandsScalarOracle(),
                "Scalar-oracle playback");

        private ScenarioMeasurement Complete(
            in StructuralPlaybackResult result,
            string executor)
        {
            if (result.DestroyedCount != _targets.Length || result.RejectedCount != 0)
                throw new InvalidOperationException($"{executor} destroy batch was not applied exactly once.");
            _hasLiveTargets = false;
            long checksum = unchecked(
                (long)result.Epoch + result.CommandCount + result.DestroyedCount);
            return new ScenarioMeasurement(_targets.Length, checksum);
        }

        public void Dispose() => _world.Dispose();

        private void CreateInitialEntities()
        {
            for (int index = 0; index < _entities.Length; index++)
            {
                if (!_world.TryCreate(_archetype, out _entities[index]))
                    throw new InvalidOperationException("Deferred-destroy fixture exhausted its source.");
            }
            FillTargets();
            _hasLiveTargets = true;
        }

        private void FillTargets()
        {
            if (ReferenceEquals(_targets, _entities))
                return;
            if (IsTailSelection(_selection))
            {
                _entities.AsSpan(_targetOffset, _targets.Length).CopyTo(_targets);
                return;
            }

            int stride = _selection switch
            {
                DeferredDestroySelection.Sparse1Percent => 100,
                DeferredDestroySelection.Sparse10Percent => 10,
                DeferredDestroySelection.Sparse50Percent => 2,
                _ => throw new InvalidOperationException(
                    "A non-tail destroy selection has no physical stride."),
            };
            for (int index = 0; index < _targets.Length; index++)
                _targets[index] = _entities[index * stride];
        }

        private static bool IsTailSelection(DeferredDestroySelection selection) =>
            selection is
                DeferredDestroySelection.Tail1Percent or
                DeferredDestroySelection.Tail10Percent or
                DeferredDestroySelection.Tail50Percent;

        private static bool IsSparseSelection(DeferredDestroySelection selection) =>
            selection is
                DeferredDestroySelection.Sparse1Percent or
                DeferredDestroySelection.Sparse10Percent or
                DeferredDestroySelection.Sparse50Percent;
    }

    /// <summary>
    /// Equal-work packed-oracle versus barrier-local destroy/create churn. Every
    /// sample rebuilds creation-order-packed ownership outside the timed region.
    /// </summary>
    private sealed class PublicBarrierLocalChurnFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly StructuralCommandBuffer _commands;
        private readonly ArchetypeHandle _archetype;
        private readonly Entity[] _entities;
        private readonly ArchetypeHandle[] _createArchetypes;
        private readonly DeferredEntity[] _tokens;
        private readonly int _stride;
        private readonly int _fixedRowBytes;
        private readonly bool _expectHoleReuse;

        internal PublicBarrierLocalChurnFixture(
            int entityCount,
            int churnPercent,
            bool wide)
        {
            if (churnPercent is not (10 or 50))
                throw new ArgumentOutOfRangeException(nameof(churnPercent));
            _stride = 100 / churnPercent;
            _expectHoleReuse = wide;
            int targetCount = entityCount / _stride;
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld(
                schema,
                new EcsWorldOptions(
                    new EntityCapacity(entityCount),
                    maximumArchetypes: 1,
                    maximumPreparedPlans: 0,
                    maximumPreparedTransitions: 0,
                    maximumStructuralCommands: targetCount * 2));
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId[] signature;
            if (wide)
            {
                signature =
                [
                    position,
                    BenchmarkSchema.GetComponentId<BenchmarkBlock512>(),
                ];
                Array.Sort(signature, static (left, right) =>
                    left.Value.CompareTo(right.Value));
                _fixedRowBytes = 524;
            }
            else
            {
                signature = [position];
                _fixedRowBytes = 12;
            }

            _archetype = _world.RegisterArchetype(
                signature,
                ChunkLayoutOptions.KiB16,
                maximumChunks: wide ? 4_096 : 128);
            _world.Seal();
            _world.Initialize();
            _world.BeginRun();
            _commands = _world.StructuralCommands;
            _entities = new Entity[entityCount];
            _createArchetypes = new ArchetypeHandle[targetCount];
            _tokens = new DeferredEntity[targetCount];
            Array.Fill(_createArchetypes, _archetype);
            CreatePackedPopulation();
        }

        internal void PrepareRecorded()
        {
            for (int index = 0; index < _entities.Length; index++)
            {
                if (!_world.TryDestroy(_entities[index]))
                    throw new InvalidOperationException(
                        "Barrier-churn fixture could not clear its prior population.");
            }
            CreatePackedPopulation();

            for (int index = 0; index < _tokens.Length; index++)
                _commands.EnqueueDestroy(_entities[index * _stride]);
            _commands.EnqueueCreateDefault(_createArchetypes, _tokens);
        }

        internal ScenarioMeasurement ExecuteRecorded() =>
            Complete(_world.PlaybackStructuralCommands(), _expectHoleReuse);

        internal ScenarioMeasurement ExecuteRecordedScalarOracle() =>
            Complete(
                _world.PlaybackStructuralCommandsScalarOracle(),
                expectHoleReuse: false);

        internal ScenarioMeasurement ExecuteRecordedPackedControl() =>
            Complete(
                _world.PlaybackStructuralCommandsPhaseIndexedPackedOracle(),
                expectHoleReuse: false);

        private ScenarioMeasurement Complete(
            in StructuralPlaybackResult result,
            bool expectHoleReuse)
        {
            int targetCount = _tokens.Length;
            if (result.DestroyedCount != targetCount ||
                result.CreatedCount != targetCount ||
                result.RejectedCount != 0)
            {
                throw new InvalidOperationException(
                    "Barrier-churn playback did not apply its balanced command set.");
            }
            if (expectHoleReuse)
            {
                if (result.TemporaryHoleCount != targetCount ||
                    result.ReusedTemporaryHoleCount != targetCount ||
                    result.CompactedTemporaryHoleCount != 0 ||
                    result.TemporaryHoleDonorRelocationCount != 0 ||
                    result.ReusedTemporaryHoleFixedRowBytes !=
                        checked((long)targetCount * _fixedRowBytes))
                {
                    throw new InvalidOperationException(
                        "Barrier-churn playback did not reuse every eligible row.");
                }
            }
            else if (result.TemporaryHoleCount != 0)
            {
                throw new InvalidOperationException(
                    "The scalar structural oracle unexpectedly reported temporary holes.");
            }

            long checksum = unchecked((long)result.Epoch + result.CommandCount);
            for (int index = 0; index < targetCount; index++)
            {
                if (!_commands.TryResolve(_tokens[index], out Entity created))
                    throw new InvalidOperationException(
                        "Barrier-churn create token did not resolve.");
                _entities[index * _stride] = created;
                checksum = unchecked(checksum + created.Index + created.Generation);
            }
            return new ScenarioMeasurement(targetCount * 2L, checksum);
        }

        private void CreatePackedPopulation()
        {
            for (int index = 0; index < _entities.Length; index++)
            {
                if (!_world.TryCreate(_archetype, out _entities[index]))
                    throw new InvalidOperationException(
                        "Barrier-churn fixture exhausted its packed population.");
            }
        }

        public void Dispose() => _world.Dispose();
    }

    private sealed class EcsWorldChunkVisitFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeComponentBinding<BenchmarkVelocity> _binding;
        private readonly int _population;

        internal EcsWorldChunkVisitFixture(int population)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(population),
                maximumArchetypes: 1,
                static capacity => CreateArena(capacity),
                maximumQueryPlans: 0);
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ArchetypeId archetype = _world.RegisterArchetype(
                [velocity],
                ChunkLayoutOptions.KiB16,
                maximumChunks: 128);
            _binding = _world.BindComponent<BenchmarkVelocity>(archetype, velocity);
            _world.Seal();
            for (int i = 0; i < population; i++)
            {
                if (!_world.TryCreateDefault(archetype, out _))
                    throw new InvalidOperationException("Chunk-query benchmark fixture exhausted unexpectedly.");
            }

            _population = population;
            var initialize = new ChunkWriteVisitor(1);
            if (_world.VisitWriteChunks(_binding, ref initialize) != population)
                throw new InvalidOperationException("Chunk-query benchmark initialization lost rows.");
        }

        internal ScenarioMeasurement ExecuteRead()
        {
            var visitor = new ChunkReadVisitor();
            int visited = _world.VisitReadChunks(_binding, ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Chunk-query read lost rows.");
            return new ScenarioMeasurement(visited, visitor.Checksum);
        }

        internal ScenarioMeasurement ExecuteWrite()
        {
            var visitor = new ChunkWriteVisitor(round: 1);
            int visited = _world.VisitWriteChunks(_binding, ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Chunk-query write lost rows.");
            return new ScenarioMeasurement(visited, visitor.Checksum);
        }

        public void Dispose() => _world.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported chunk-query benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }

        private struct ChunkReadVisitor : IComponentChunkReadVisitor<BenchmarkVelocity>
        {
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadOnlyComponentChunk<BenchmarkVelocity> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(chunk.Components[row].X));
                    checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(chunk.Components[row].Y));
                    checksum += chunk.IsEnabled(row) ? 1 : 0;
                }
                Checksum = checksum;
            }
        }

        private struct ChunkWriteVisitor : IComponentChunkWriteVisitor<BenchmarkVelocity>
        {
            private readonly int _round;

            internal ChunkWriteVisitor(int round)
            {
                _round = round;
                Checksum = 0;
            }

            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in WritableComponentChunk<BenchmarkVelocity> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    uint entityIndex = chunk.EntityIndices[row];
                    float x = entityIndex + _round;
                    float y = _round - entityIndex;
                    chunk.Components[row] = new BenchmarkVelocity { X = x, Y = y };
                    bool enabled = ((entityIndex + (uint)_round) & 1) == 0;
                    chunk.SetEnabled(row, enabled);
                    checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(x));
                    checksum += enabled ? 1 : 0;
                }
                Checksum = checksum;
            }
        }
    }

    private sealed class EcsWorldReadWriteQueryFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadWriteQueryPlan<TArena, BenchmarkPosition, BenchmarkVelocity> _plan;
        private readonly int _population;

        internal EcsWorldReadWriteQueryFixture(int population)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(population),
                maximumArchetypes: 2,
                static capacity => CreateArena(capacity),
                maximumQueryPlans: 1);
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ComponentId[] firstSignature = CreateSortedSignature(position, velocity);
            ComponentId[] secondSignature = CreateSortedSignature(position, velocity, wide);
            ArchetypeId first = _world.RegisterArchetype(
                firstSignature,
                ChunkLayoutOptions.KiB16,
                maximumChunks: 128);
            ArchetypeId second = _world.RegisterArchetype(
                secondSignature,
                ChunkLayoutOptions.KiB16,
                maximumChunks: 512);
            ArchetypeComponentBinding<BenchmarkPosition> firstPosition =
                _world.BindComponent<BenchmarkPosition>(first, position);
            ArchetypeComponentBinding<BenchmarkPosition> secondPosition =
                _world.BindComponent<BenchmarkPosition>(second, position);
            _plan = _world.PrepareReadWriteQuery<BenchmarkPosition, BenchmarkVelocity>(
                position,
                velocity);
            _world.Seal();

            for (int i = 0; i < population; i++)
            {
                ArchetypeId archetype = (i & 1) == 0 ? first : second;
                ArchetypeComponentBinding<BenchmarkPosition> binding = (i & 1) == 0
                    ? firstPosition
                    : secondPosition;
                if (!_world.TryCreateInitialized(
                    archetype,
                    binding,
                    new BenchmarkPosition { X = i, Y = -i },
                    out _))
                {
                    throw new InvalidOperationException("Read/write query benchmark fixture exhausted unexpectedly.");
                }
            }

            if (_plan.MatchedArchetypeCount != 2)
                throw new InvalidOperationException("Read/write query benchmark did not compile both archetypes.");
            _population = population;
        }

        internal ScenarioMeasurement Execute()
        {
            var visitor = new PositionVelocityVisitor();
            int visited = _world.VisitReadWriteChunks(_plan, ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Read/write query benchmark lost rows.");
            return new ScenarioMeasurement(visited, visitor.Checksum);
        }

        public void Dispose() => _world.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported read/write query benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }

        private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
        {
            Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
            return componentIds;
        }

        private struct PositionVelocityVisitor :
            IComponentChunkReadWriteVisitor<BenchmarkPosition, BenchmarkVelocity>
        {
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadWriteComponentChunk<BenchmarkPosition, BenchmarkVelocity> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    float x = chunk.ReadComponents[row].X;
                    float y = chunk.ReadComponents[row].Y;
                    chunk.WriteComponents[row] = new BenchmarkVelocity
                    {
                        X = x * 2f,
                        Y = y * 2f,
                    };
                    bool enabled = (chunk.EntityIndices[row] & 1) == 0;
                    chunk.SetWriteEnabled(row, enabled);
                    checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(x));
                    checksum += enabled ? 1 : 0;
                }
                Checksum = checksum;
            }
        }
    }

    private sealed class EcsWorldGeneratedQueryFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadReadWriteQueryPlan<
            TArena,
            BenchmarkPosition,
            BenchmarkWide,
            BenchmarkVelocity> _plan;
        private readonly int _population;

        internal EcsWorldGeneratedQueryFixture(int population)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(population),
                maximumArchetypes: 2,
                static capacity => CreateArena(capacity),
                maximumQueryPlans: 1);
            ComponentId disabled = BenchmarkSchema.GetComponentId<BenchmarkDisabled>();
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ArchetypeId first = _world.RegisterArchetype(
                CreateSortedSignature(position, velocity, wide),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 512);
            ArchetypeId second = _world.RegisterArchetype(
                CreateSortedSignature(disabled, position, velocity, wide),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 512);
            ArchetypeComponentBinding<BenchmarkPosition> firstPosition =
                _world.BindComponent<BenchmarkPosition>(first, position);
            ArchetypeComponentBinding<BenchmarkPosition> secondPosition =
                _world.BindComponent<BenchmarkPosition>(second, position);
            ArchetypeComponentBinding<BenchmarkWide> firstWide =
                _world.BindComponent<BenchmarkWide>(first, wide);
            ArchetypeComponentBinding<BenchmarkWide> secondWide =
                _world.BindComponent<BenchmarkWide>(second, wide);
            _plan = _world.PrepareReadReadWriteQuery<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkVelocity>(position, wide, velocity);
            _world.Seal();

            for (int i = 0; i < population; i++)
            {
                bool useFirst = (i & 1) == 0;
                ArchetypeId archetype = useFirst ? first : second;
                ArchetypeComponentBinding<BenchmarkPosition> positionBinding = useFirst
                    ? firstPosition
                    : secondPosition;
                ArchetypeComponentBinding<BenchmarkWide> wideBinding = useFirst
                    ? firstWide
                    : secondWide;
                if (!_world.TryCreateInitialized(
                    archetype,
                    positionBinding,
                    new BenchmarkPosition { X = i, Y = -i },
                    out Entity entity) ||
                    !_world.TrySetComponent(
                        entity,
                        wideBinding,
                        new BenchmarkWide
                        {
                            A = i + 1L,
                            B = i + 2L,
                            C = i + 3L,
                            D = i + 4L,
                            E = i + 5L,
                            F = i + 6L,
                            G = i + 7L,
                            H = i + 8L,
                        }))
                {
                    throw new InvalidOperationException(
                        "Generated-query benchmark fixture exhausted unexpectedly.");
                }
            }

            if (_plan.MatchedArchetypeCount != 2)
                throw new InvalidOperationException("Generated-query benchmark did not compile both archetypes.");
            _population = population;
        }

        internal ScenarioMeasurement Execute()
        {
            var visitor = new PositionWideVelocityVisitor();
            int visited = _world.VisitReadReadWriteChunks(_plan, ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Generated-query benchmark lost rows.");
            return new ScenarioMeasurement(visited, visitor.Checksum);
        }

        public void Dispose() => _world.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported generated-query benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }

        private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
        {
            Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
            return componentIds;
        }

        private struct PositionWideVelocityVisitor :
            IComponentChunkReadReadWriteVisitor<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>
        {
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadReadWriteComponentChunk<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkVelocity> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    ref readonly BenchmarkPosition position = ref chunk.ReadComponents1[row];
                    ref readonly BenchmarkWide wide = ref chunk.ReadComponents2[row];
                    long wideSum = unchecked(
                        wide.A + wide.B + wide.C + wide.D +
                        wide.E + wide.F + wide.G + wide.H);
                    float scale = (wideSum & 7L) + 1f;
                    chunk.WriteComponents[row] = new BenchmarkVelocity
                    {
                        X = position.X * scale,
                        Y = position.Y * scale,
                    };
                    bool enabled = (chunk.EntityIndices[row] & 1) == 0;
                    chunk.SetWriteEnabled(row, enabled);
                    checksum = unchecked(
                        checksum + wideSum + BitConverter.SingleToInt32Bits(position.X));
                    checksum += enabled ? 1 : 0;
                }
                Checksum = checksum;
            }
        }
    }

    private sealed class EcsWorldGeneratedReadQueryFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadReadQueryPlan<
            TArena,
            BenchmarkPosition,
            BenchmarkWide> _plan;
        private readonly int _population;

        internal EcsWorldGeneratedReadQueryFixture(int population)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(population),
                maximumArchetypes: 2,
                static capacity => CreateArena(capacity),
                maximumQueryPlans: 1);
            ComponentId disabled = BenchmarkSchema.GetComponentId<BenchmarkDisabled>();
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ArchetypeId first = _world.RegisterArchetype(
                CreateSortedSignature(position, velocity, wide),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 512);
            ArchetypeId second = _world.RegisterArchetype(
                CreateSortedSignature(disabled, position, velocity, wide),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 512);
            ArchetypeComponentBinding<BenchmarkPosition> firstPosition =
                _world.BindComponent<BenchmarkPosition>(first, position);
            ArchetypeComponentBinding<BenchmarkPosition> secondPosition =
                _world.BindComponent<BenchmarkPosition>(second, position);
            ArchetypeComponentBinding<BenchmarkWide> firstWide =
                _world.BindComponent<BenchmarkWide>(first, wide);
            ArchetypeComponentBinding<BenchmarkWide> secondWide =
                _world.BindComponent<BenchmarkWide>(second, wide);
            _plan = _world.PrepareReadReadQuery<BenchmarkPosition, BenchmarkWide>(position, wide);
            _world.Seal();

            for (int i = 0; i < population; i++)
            {
                bool useFirst = (i & 1) == 0;
                ArchetypeId archetype = useFirst ? first : second;
                ArchetypeComponentBinding<BenchmarkPosition> positionBinding = useFirst
                    ? firstPosition
                    : secondPosition;
                ArchetypeComponentBinding<BenchmarkWide> wideBinding = useFirst
                    ? firstWide
                    : secondWide;
                if (!_world.TryCreateInitialized(
                    archetype,
                    positionBinding,
                    new BenchmarkPosition { X = i, Y = -i },
                    out Entity entity) ||
                    !_world.TrySetComponent(
                        entity,
                        wideBinding,
                        new BenchmarkWide
                        {
                            A = i + 1L,
                            B = i + 2L,
                            C = i + 3L,
                            D = i + 4L,
                            E = i + 5L,
                            F = i + 6L,
                            G = i + 7L,
                            H = i + 8L,
                        }))
                {
                    throw new InvalidOperationException(
                        "Generated-read-query benchmark fixture exhausted unexpectedly.");
                }
            }

            if (_plan.MatchedArchetypeCount != 2)
                throw new InvalidOperationException("Generated-read-query benchmark did not compile both archetypes.");
            _population = population;
        }

        internal ScenarioMeasurement Execute()
        {
            var visitor = new PositionWideReadVisitor();
            int visited = _world.VisitReadReadChunks(_plan, ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Generated-read-query benchmark lost rows.");
            return new ScenarioMeasurement(visited, visitor.Checksum);
        }

        public void Dispose() => _world.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported generated-read-query benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }

        private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
        {
            Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
            return componentIds;
        }

        private struct PositionWideReadVisitor :
            IComponentChunkReadReadVisitor<BenchmarkPosition, BenchmarkWide>
        {
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadReadComponentChunk<BenchmarkPosition, BenchmarkWide> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    ref readonly BenchmarkPosition position = ref chunk.ReadComponents1[row];
                    ref readonly BenchmarkWide wide = ref chunk.ReadComponents2[row];
                    checksum = unchecked(
                        checksum +
                        wide.A + wide.B + wide.C + wide.D +
                        wide.E + wide.F + wide.G + wide.H +
                        BitConverter.SingleToInt32Bits(position.X) +
                        BitConverter.SingleToInt32Bits(position.Y) +
                        chunk.EntityIndices[row]);
                }
                Checksum = checksum;
            }
        }
    }

    private sealed class EcsWorldGeneratedWideQueryFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeStorage<TArena> _firstStorage;
        private readonly ArchetypeStorage<TArena> _secondStorage;
        private readonly ArchetypeComponentBinding<BenchmarkPosition> _firstPosition;
        private readonly ArchetypeComponentBinding<BenchmarkPosition> _secondPosition;
        private readonly ChangeVersion _scanBaseline;
        private readonly ArchetypeReadReadReadWriteQueryPlan<
            TArena,
            BenchmarkPosition,
            BenchmarkWide,
            BenchmarkScalar,
            BenchmarkVelocity> _threeReadPlan;
        private readonly ArchetypeReadReadWriteWriteQueryPlan<
            TArena,
            BenchmarkPosition,
            BenchmarkScalar,
            BenchmarkWide,
            BenchmarkVelocity> _balancedPlan;
        private readonly ArchetypeReadReadWriteWriteQueryPlan<
            TArena,
            BenchmarkPosition,
            BenchmarkWide,
            BenchmarkScalar,
            BenchmarkVelocity> _changePreparationPlan;
        private readonly ArchetypeChangedReadQueryPlan<TArena, BenchmarkWide> _changedReadPlan;
        private readonly ArchetypeChangedReadQueryPlan<TArena, BenchmarkWide> _changedRowsPlan;
        private readonly ArchetypeReadReadChangedQueryPlan<
            TArena,
            BenchmarkPosition,
            BenchmarkWide> _multiAnyPlan;
        private readonly ArchetypeReadReadChangedQueryPlan<
            TArena,
            BenchmarkPosition,
            BenchmarkWide> _multiAllPlan;
        private readonly ArchetypeComponentBinding<BenchmarkScalar> _sparseScalarBinding;
        private readonly Entity _sparseEntity;
        private ChangeCursor _unchangedCursor;
        private ChangeCursor _changedCursor;
        private ChangeCursor _unchangedRowsCursor;
        private ChangeCursor _oneChangedRowCursor;
        private ChangeCursor _multiAnyCursor;
        private ChangeCursor _multiAllCursor;
        private readonly int _occupiedChunkCount;
        private readonly int _population;

        internal EcsWorldGeneratedWideQueryFixture(int population)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(population),
                maximumArchetypes: 2,
                static capacity => CreateArena(capacity),
                maximumQueryPlans: 7);
            ComponentId disabled = BenchmarkSchema.GetComponentId<BenchmarkDisabled>();
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId scalar = BenchmarkSchema.GetComponentId<BenchmarkScalar>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ArchetypeId first = _world.RegisterArchetype(
                CreateSortedSignature(position, scalar, velocity, wide),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 512);
            ArchetypeId second = _world.RegisterArchetype(
                CreateSortedSignature(disabled, position, scalar, velocity, wide),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 512);
            _firstStorage = _world.GetArchetypeStorage(first);
            _secondStorage = _world.GetArchetypeStorage(second);
            ArchetypeComponentBinding<BenchmarkPosition> firstPosition =
                _world.BindComponent<BenchmarkPosition>(first, position);
            ArchetypeComponentBinding<BenchmarkPosition> secondPosition =
                _world.BindComponent<BenchmarkPosition>(second, position);
            _firstPosition = firstPosition;
            _secondPosition = secondPosition;
            ArchetypeComponentBinding<BenchmarkScalar> firstScalar =
                _world.BindComponent<BenchmarkScalar>(first, scalar);
            ArchetypeComponentBinding<BenchmarkScalar> secondScalar =
                _world.BindComponent<BenchmarkScalar>(second, scalar);
            ArchetypeComponentBinding<BenchmarkWide> firstWide =
                _world.BindComponent<BenchmarkWide>(first, wide);
            ArchetypeComponentBinding<BenchmarkWide> secondWide =
                _world.BindComponent<BenchmarkWide>(second, wide);
            _threeReadPlan = _world.PrepareReadReadReadWriteQuery<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkScalar,
                BenchmarkVelocity>(position, wide, scalar, velocity);
            _balancedPlan = _world.PrepareReadReadWriteWriteQuery<
                BenchmarkPosition,
                BenchmarkScalar,
                BenchmarkWide,
                BenchmarkVelocity>(position, scalar, wide, velocity);
            _changePreparationPlan = _world.PrepareReadReadWriteWriteQuery<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkScalar,
                BenchmarkVelocity>(position, wide, scalar, velocity);
            _changedReadPlan = _world.PrepareChangedReadQuery<BenchmarkWide>(wide, scalar);
            _changedRowsPlan = _world.PrepareChangedRowsQuery<BenchmarkWide>(wide, scalar);
            _multiAnyPlan = _world.PrepareReadReadChangedQuery<
                BenchmarkPosition,
                BenchmarkWide>(
                    position,
                    wide,
                    scalar,
                    velocity,
                    ChangedMatchMode.Any);
            _multiAllPlan = _world.PrepareReadReadChangedQuery<
                BenchmarkPosition,
                BenchmarkWide>(
                    position,
                    wide,
                    scalar,
                    velocity,
                    ChangedMatchMode.All);
            _world.Seal();

            Entity sparseEntity = default;
            for (int i = 0; i < population; i++)
            {
                bool useFirst = (i & 1) == 0;
                ArchetypeId archetype = useFirst ? first : second;
                ArchetypeComponentBinding<BenchmarkPosition> positionBinding = useFirst
                    ? firstPosition
                    : secondPosition;
                ArchetypeComponentBinding<BenchmarkScalar> scalarBinding = useFirst
                    ? firstScalar
                    : secondScalar;
                ArchetypeComponentBinding<BenchmarkWide> wideBinding = useFirst
                    ? firstWide
                    : secondWide;
                if (!_world.TryCreateInitialized(
                    archetype,
                    positionBinding,
                    new BenchmarkPosition { X = i, Y = -i },
                    out Entity entity) ||
                    !_world.TrySetComponent(
                        entity,
                        scalarBinding,
                        new BenchmarkScalar { Value = i * 3 + 1 }) ||
                    !_world.TrySetComponent(
                        entity,
                        wideBinding,
                        new BenchmarkWide
                        {
                            A = i + 1L,
                            B = i + 2L,
                            C = i + 3L,
                            D = i + 4L,
                            E = i + 5L,
                            F = i + 6L,
                            G = i + 7L,
                            H = i + 8L,
                        }))
                {
                    throw new InvalidOperationException(
                        "Generated-wide-query benchmark fixture exhausted unexpectedly.");
                }
                if (i == 0)
                    sparseEntity = entity;
            }

            if (_threeReadPlan.MatchedArchetypeCount != 2 ||
                _balancedPlan.MatchedArchetypeCount != 2 ||
                _changePreparationPlan.MatchedArchetypeCount != 2 ||
                _changedReadPlan.MatchedArchetypeCount != 2 ||
                _changedRowsPlan.MatchedArchetypeCount != 2 ||
                _multiAnyPlan.MatchedArchetypeCount != 2 ||
                _multiAllPlan.MatchedArchetypeCount != 2)
            {
                throw new InvalidOperationException(
                    "Generated-wide-query benchmark did not compile both archetypes.");
            }
            _scanBaseline = _world.CurrentChangeVersion;
            _unchangedCursor = _world.CreateChangeCursorFromNow();
            _changedCursor = _unchangedCursor;
            _unchangedRowsCursor = _unchangedCursor;
            _oneChangedRowCursor = _unchangedCursor;
            _multiAnyCursor = _unchangedCursor;
            _multiAllCursor = _unchangedCursor;
            _sparseScalarBinding = firstScalar;
            _sparseEntity = sparseEntity;
            _occupiedChunkCount = CountOccupiedChunks(_firstStorage) +
                CountOccupiedChunks(_secondStorage);
            _population = population;
        }

        internal ScenarioMeasurement ExecuteThreeRead()
        {
            var visitor = new PositionWideScalarVelocityVisitor();
            int visited = _world.VisitReadReadReadWriteChunks(_threeReadPlan, ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Generated 3R1W benchmark lost rows.");
            return new ScenarioMeasurement(visited, visitor.Checksum);
        }

        internal long ExactChangedRowsMetadataBytes => checked(
            _firstStorage.Store.GetSnapshot().SparseRowChangeVersionMetadataBytes +
            _secondStorage.Store.GetSnapshot().SparseRowChangeVersionMetadataBytes);

        internal ScenarioMeasurement ExecuteBalanced()
        {
            var visitor = new PositionScalarWideVelocityVisitor();
            int visited = _world.VisitReadReadWriteWriteChunks(_balancedPlan, ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Generated 2R2W benchmark lost rows.");
            return new ScenarioMeasurement(visited, visitor.Checksum);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        internal ScenarioMeasurement ExecuteUnchangedVersionScan()
        {
            const int scanCount = 64;
            int scanned = 0;
            long checksum = 0;
            for (int scan = 0; scan < scanCount; scan++)
            {
                for (int chunk = _firstStorage.Store.GetFirstOccupiedChunkIndex();
                     chunk >= 0;
                     chunk = _firstStorage.Store.GetNextOccupiedChunkIndex(chunk))
                {
                    bool changed = _world.HasChunkChangedSince(
                        _firstPosition,
                        (uint)chunk,
                        _scanBaseline);
                    checksum = unchecked(checksum + (changed ? 1 : 0) + chunk + scan);
                    scanned++;
                }
                for (int chunk = _secondStorage.Store.GetFirstOccupiedChunkIndex();
                     chunk >= 0;
                     chunk = _secondStorage.Store.GetNextOccupiedChunkIndex(chunk))
                {
                    bool changed = _world.HasChunkChangedSince(
                        _secondPosition,
                        (uint)chunk,
                        _scanBaseline);
                    checksum = unchecked(checksum + (changed ? 1 : 0) + chunk + scan);
                    scanned++;
                }
            }
            return new ScenarioMeasurement(scanned, checksum);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        internal ScenarioMeasurement ExecuteUnchangedChangedRead()
        {
            const int scanCount = 64;
            int visited = 0;
            var visitor = new WideChangedReadVisitor();
            for (int scan = 0; scan < scanCount; scan++)
            {
                visited += _world.VisitChangedReadChunks(
                    _changedReadPlan,
                    ref _unchangedCursor,
                    ref visitor);
            }
            if (visited != 0 || visitor.Rows != 0)
                throw new InvalidOperationException("Unchanged changed-read benchmark returned rows.");
            return new ScenarioMeasurement(
                (long)_occupiedChunkCount * scanCount,
                unchecked((long)_unchangedCursor.Version.Value + visitor.Checksum));
        }

        internal void PrepareAllChangedRead()
        {
            _changedCursor = _world.CreateChangeCursorFromNow();
            var visitor = new ScalarVelocityChangeWriter();
            int visited = _world.VisitReadReadWriteWriteChunks(
                _changePreparationPlan,
                ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Changed-read preparation lost rows.");
        }

        internal ScenarioMeasurement ExecuteAllChangedRead()
        {
            var visitor = new WideChangedReadVisitor();
            int visited = _world.VisitChangedReadChunks(
                _changedReadPlan,
                ref _changedCursor,
                ref visitor);
            if (visited != _population || visitor.Rows != _population)
                throw new InvalidOperationException("All-changed changed-read benchmark lost rows.");
            return new ScenarioMeasurement(
                visited,
                unchecked(visitor.Checksum + (long)_changedCursor.Version.Value));
        }

        internal void PrepareUnchangedMultiAny() =>
            _multiAnyCursor = _world.CreateChangeCursorFromNow();

        internal void PrepareUnchangedMultiAll() =>
            _multiAllCursor = _world.CreateChangeCursorFromNow();

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        internal ScenarioMeasurement ExecuteUnchangedMultiAny() =>
            ExecuteUnchangedMulti(_multiAnyPlan, ref _multiAnyCursor);

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        internal ScenarioMeasurement ExecuteUnchangedMultiAll() =>
            ExecuteUnchangedMulti(_multiAllPlan, ref _multiAllCursor);

        internal void PrepareAllChangedMultiAny()
        {
            _multiAnyCursor = _world.CreateChangeCursorFromNow();
            WriteBothMultiWatchedColumns();
        }

        internal void PrepareAllChangedMultiAll()
        {
            _multiAllCursor = _world.CreateChangeCursorFromNow();
            WriteBothMultiWatchedColumns();
        }

        internal ScenarioMeasurement ExecuteAllChangedMultiAny() =>
            ExecuteAllChangedMulti(_multiAnyPlan, ref _multiAnyCursor);

        internal ScenarioMeasurement ExecuteAllChangedMultiAll() =>
            ExecuteAllChangedMulti(_multiAllPlan, ref _multiAllCursor);

        private ScenarioMeasurement ExecuteUnchangedMulti(
            ArchetypeReadReadChangedQueryPlan<
                TArena,
                BenchmarkPosition,
                BenchmarkWide> plan,
            ref ChangeCursor cursor)
        {
            const int scanCount = 64;
            int visited = 0;
            var visitor = new PositionWideChangedVisitor();
            for (int scan = 0; scan < scanCount; scan++)
            {
                visited += _world.VisitReadReadChangedChunks(
                    plan,
                    ref cursor,
                    ref visitor);
            }
            if (visited != 0 || visitor.Rows != 0)
                throw new InvalidOperationException("Unchanged multi-column query returned rows.");
            return new ScenarioMeasurement(
                (long)_occupiedChunkCount * scanCount,
                unchecked((long)cursor.Version.Value + visitor.Checksum));
        }

        private ScenarioMeasurement ExecuteAllChangedMulti(
            ArchetypeReadReadChangedQueryPlan<
                TArena,
                BenchmarkPosition,
                BenchmarkWide> plan,
            ref ChangeCursor cursor)
        {
            var visitor = new PositionWideChangedVisitor();
            int visited = _world.VisitReadReadChangedChunks(
                plan,
                ref cursor,
                ref visitor);
            if (visited != _population || visitor.Rows != _population)
                throw new InvalidOperationException("All-changed multi-column query lost rows.");
            return new ScenarioMeasurement(
                visited,
                unchecked(visitor.Checksum + (long)cursor.Version.Value));
        }

        private void WriteBothMultiWatchedColumns()
        {
            var visitor = new ScalarVelocityChangeWriter();
            int visited = _world.VisitReadReadWriteWriteChunks(
                _changePreparationPlan,
                ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Multi-column change preparation lost rows.");
        }

        internal void PrepareUnchangedChangedRows() =>
            _unchangedRowsCursor = _world.CreateChangeCursorFromNow();

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        internal ScenarioMeasurement ExecuteUnchangedChangedRows()
        {
            const int scanCount = 64;
            int visited = 0;
            var visitor = new WideChangedReadVisitor();
            for (int scan = 0; scan < scanCount; scan++)
            {
                visited += _world.VisitChangedReadRows(
                    _changedRowsPlan,
                    ref _unchangedRowsCursor,
                    ref visitor);
            }
            if (visited != 0 || visitor.Rows != 0)
                throw new InvalidOperationException("Unchanged exact-row benchmark returned rows.");
            return new ScenarioMeasurement(
                (long)_occupiedChunkCount * scanCount,
                unchecked((long)_unchangedRowsCursor.Version.Value + visitor.Checksum));
        }

        internal void PrepareOneChangedRow()
        {
            _oneChangedRowCursor = _world.CreateChangeCursorFromNow();
            if (!_world.TrySetComponent(
                _sparseEntity,
                _sparseScalarBinding,
                new BenchmarkScalar
                {
                    Value = unchecked((int)_world.CurrentChangeVersion.Value + 1),
                }))
            {
                throw new InvalidOperationException("Exact-row benchmark preparation lost its entity.");
            }
        }

        internal ScenarioMeasurement ExecuteOneChangedRow()
        {
            var visitor = new WideChangedReadVisitor();
            int visited = _world.VisitChangedReadRows(
                _changedRowsPlan,
                ref _oneChangedRowCursor,
                ref visitor);
            if (visited != 1 || visitor.Rows != 1)
                throw new InvalidOperationException("One-row exact changed-read benchmark lost precision.");
            return new ScenarioMeasurement(
                visited,
                unchecked(visitor.Checksum + (long)_oneChangedRowCursor.Version.Value));
        }

        public void Dispose() => _world.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported generated-wide-query benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }

        private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
        {
            Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
            return componentIds;
        }

        private static int CountOccupiedChunks(ArchetypeStorage<TArena> storage)
        {
            int count = 0;
            for (int chunk = storage.Store.GetFirstOccupiedChunkIndex();
                 chunk >= 0;
                 chunk = storage.Store.GetNextOccupiedChunkIndex(chunk))
            {
                count++;
            }
            return count;
        }

        private struct WideChangedReadVisitor : IComponentChunkReadVisitor<BenchmarkWide>
        {
            internal int Rows;
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadOnlyComponentChunk<BenchmarkWide> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    ref readonly BenchmarkWide wide = ref chunk.Components[row];
                    checksum = unchecked(
                        checksum + wide.A + wide.B + wide.C + wide.D +
                        wide.E + wide.F + wide.G + wide.H + chunk.EntityIndices[row]);
                }
                Rows += chunk.Count;
                Checksum = checksum;
            }
        }

        private struct PositionWideChangedVisitor :
            IComponentChunkReadReadVisitor<BenchmarkPosition, BenchmarkWide>
        {
            internal int Rows;
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadReadComponentChunk<BenchmarkPosition, BenchmarkWide> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    ref readonly BenchmarkPosition position = ref chunk.ReadComponents1[row];
                    ref readonly BenchmarkWide wide = ref chunk.ReadComponents2[row];
                    checksum = unchecked(
                        checksum +
                        wide.A + wide.B + wide.C + wide.D +
                        wide.E + wide.F + wide.G + wide.H +
                        BitConverter.SingleToInt32Bits(position.X) +
                        BitConverter.SingleToInt32Bits(position.Y) +
                        chunk.EntityIndices[row]);
                }
                Rows += chunk.Count;
                Checksum = checksum;
            }
        }

        private struct ScalarVelocityChangeWriter :
            IComponentChunkReadReadWriteWriteVisitor<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkScalar,
                BenchmarkVelocity>
        {
            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadReadWriteWriteComponentChunk<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkScalar,
                BenchmarkVelocity> chunk)
            {
                for (int row = 0; row < chunk.Count; row++)
                {
                    ref readonly BenchmarkPosition position = ref chunk.ReadComponents1[row];
                    ref readonly BenchmarkWide wide = ref chunk.ReadComponents2[row];
                    chunk.WriteComponents1[row] = new BenchmarkScalar
                    {
                        Value = unchecked((int)(wide.A + chunk.EntityIndices[row])),
                    };
                    chunk.WriteComponents2[row] = new BenchmarkVelocity
                    {
                        X = position.X + row,
                        Y = position.Y - row,
                    };
                }
            }
        }

        private struct PositionWideScalarVelocityVisitor :
            IComponentChunkReadReadReadWriteVisitor<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkScalar,
                BenchmarkVelocity>
        {
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadReadReadWriteComponentChunk<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkScalar,
                BenchmarkVelocity> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    ref readonly BenchmarkPosition position = ref chunk.ReadComponents1[row];
                    ref readonly BenchmarkWide wide = ref chunk.ReadComponents2[row];
                    int scalar = chunk.ReadComponents3[row].Value;
                    long wideSum = unchecked(
                        wide.A + wide.B + wide.C + wide.D +
                        wide.E + wide.F + wide.G + wide.H);
                    float scale = ((wideSum + scalar) & 7L) + 1f;
                    chunk.WriteComponents[row] = new BenchmarkVelocity
                    {
                        X = position.X * scale,
                        Y = position.Y * scale,
                    };
                    bool enabled = (chunk.EntityIndices[row] & 1) == 0;
                    chunk.SetWriteEnabled(row, enabled);
                    checksum = unchecked(
                        checksum + wideSum + scalar +
                        BitConverter.SingleToInt32Bits(position.X) +
                        BitConverter.SingleToInt32Bits(position.Y) +
                        (enabled ? 1 : 0));
                }
                Checksum = checksum;
            }
        }

        private struct PositionScalarWideVelocityVisitor :
            IComponentChunkReadReadWriteWriteVisitor<
                BenchmarkPosition,
                BenchmarkScalar,
                BenchmarkWide,
                BenchmarkVelocity>
        {
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadReadWriteWriteComponentChunk<
                BenchmarkPosition,
                BenchmarkScalar,
                BenchmarkWide,
                BenchmarkVelocity> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    ref readonly BenchmarkPosition position = ref chunk.ReadComponents1[row];
                    int scalar = chunk.ReadComponents2[row].Value;
                    long seed = unchecked(
                        BitConverter.SingleToInt32Bits(position.X) +
                        ((long)BitConverter.SingleToInt32Bits(position.Y) << 1) +
                        scalar + chunk.EntityIndices[row]);
                    chunk.WriteComponents1[row] = new BenchmarkWide
                    {
                        A = seed + 1,
                        B = seed + 2,
                        C = seed + 3,
                        D = seed + 4,
                        E = seed + 5,
                        F = seed + 6,
                        G = seed + 7,
                        H = seed + 8,
                    };
                    chunk.WriteComponents2[row] = new BenchmarkVelocity
                    {
                        X = position.X * scalar,
                        Y = position.Y * scalar,
                    };
                    bool enabled = (chunk.EntityIndices[row] & 1) == 0;
                    chunk.SetWrite2Enabled(row, enabled);
                    checksum = unchecked(checksum + seed + scalar + (enabled ? 1 : 0));
                }
                Checksum = checksum;
            }
        }
    }

    private sealed class EcsWorldGeneratedWriteQueryFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadWriteWriteQueryPlan<
            TArena,
            BenchmarkPosition,
            BenchmarkWide,
            BenchmarkVelocity> _plan;
        private readonly int _population;

        internal EcsWorldGeneratedWriteQueryFixture(int population)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(population),
                maximumArchetypes: 2,
                static capacity => CreateArena(capacity),
                maximumQueryPlans: 1);
            ComponentId disabled = BenchmarkSchema.GetComponentId<BenchmarkDisabled>();
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ArchetypeId first = _world.RegisterArchetype(
                CreateSortedSignature(position, velocity, wide),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 512);
            ArchetypeId second = _world.RegisterArchetype(
                CreateSortedSignature(disabled, position, velocity, wide),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 512);
            ArchetypeComponentBinding<BenchmarkPosition> firstPosition =
                _world.BindComponent<BenchmarkPosition>(first, position);
            ArchetypeComponentBinding<BenchmarkPosition> secondPosition =
                _world.BindComponent<BenchmarkPosition>(second, position);
            _plan = _world.PrepareReadWriteWriteQuery<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkVelocity>(position, wide, velocity);
            _world.Seal();

            for (int i = 0; i < population; i++)
            {
                bool useFirst = (i & 1) == 0;
                ArchetypeId archetype = useFirst ? first : second;
                ArchetypeComponentBinding<BenchmarkPosition> binding = useFirst
                    ? firstPosition
                    : secondPosition;
                if (!_world.TryCreateInitialized(
                    archetype,
                    binding,
                    new BenchmarkPosition { X = i, Y = -i },
                    out _))
                {
                    throw new InvalidOperationException(
                        "Generated-write-query benchmark fixture exhausted unexpectedly.");
                }
            }

            if (_plan.MatchedArchetypeCount != 2)
                throw new InvalidOperationException("Generated-write-query benchmark did not compile both archetypes.");
            _population = population;
        }

        internal ScenarioMeasurement Execute()
        {
            var visitor = new PositionWideVelocityWriteVisitor();
            int visited = _world.VisitReadWriteWriteChunks(_plan, ref visitor);
            if (visited != _population)
                throw new InvalidOperationException("Generated-write-query benchmark lost rows.");
            return new ScenarioMeasurement(visited, visitor.Checksum);
        }

        public void Dispose() => _world.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported generated-write-query benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }

        private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
        {
            Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
            return componentIds;
        }

        private struct PositionWideVelocityWriteVisitor :
            IComponentChunkReadWriteWriteVisitor<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>
        {
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadWriteWriteComponentChunk<
                BenchmarkPosition,
                BenchmarkWide,
                BenchmarkVelocity> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    ref readonly BenchmarkPosition position = ref chunk.ReadComponents[row];
                    long seed = unchecked(
                        BitConverter.SingleToInt32Bits(position.X) +
                        ((long)BitConverter.SingleToInt32Bits(position.Y) << 1) +
                        chunk.EntityIndices[row]);
                    chunk.WriteComponents1[row] = new BenchmarkWide
                    {
                        A = seed + 1,
                        B = seed + 2,
                        C = seed + 3,
                        D = seed + 4,
                        E = seed + 5,
                        F = seed + 6,
                        G = seed + 7,
                        H = seed + 8,
                    };
                    chunk.WriteComponents2[row] = new BenchmarkVelocity
                    {
                        X = position.X * 2f,
                        Y = position.Y * 2f,
                    };
                    bool enabled = (chunk.EntityIndices[row] & 1) == 0;
                    chunk.SetWrite2Enabled(row, enabled);
                    checksum = unchecked(checksum + seed + (enabled ? 1 : 0));
                }
                Checksum = checksum;
            }
        }
    }

    private sealed class EcsWorldFilteredReadWriteQueryFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadWriteQueryPlan<TArena, BenchmarkPosition, BenchmarkVelocity> _plan;
        private readonly ArchetypeComponentBinding<BenchmarkVelocity> _velocityBinding;
        private readonly Entity[] _entities;
        private int _expectedMatches;

        internal EcsWorldFilteredReadWriteQueryFixture(int population)
        {
            ComponentSchema schema = BenchmarkSchema.CreateSchema();
            _world = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(population),
                maximumArchetypes: 3,
                static capacity => CreateArena(capacity),
                maximumQueryPlans: 1);
            ComponentId disabled = BenchmarkSchema.GetComponentId<BenchmarkDisabled>();
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId wide = BenchmarkSchema.GetComponentId<BenchmarkWide>();
            ArchetypeId matching = _world.RegisterArchetype(
                CreateSortedSignature(disabled, position, velocity),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 128);
            _ = _world.RegisterArchetype(
                CreateSortedSignature(position, velocity),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 1);
            _ = _world.RegisterArchetype(
                CreateSortedSignature(disabled, position, velocity, wide),
                ChunkLayoutOptions.KiB16,
                maximumChunks: 1);
            ArchetypeComponentBinding<BenchmarkPosition> positionBinding =
                _world.BindComponent<BenchmarkPosition>(matching, position);
            _velocityBinding = _world.BindComponent<BenchmarkVelocity>(matching, velocity);
            ArchetypeQueryFilter filter = _world.CreateQueryFilter(
                ReadOnlySpan<ComponentId>.Empty,
                [wide],
                [disabled],
                [velocity]);
            _plan = _world.PrepareReadWriteQuery<BenchmarkPosition, BenchmarkVelocity>(
                position,
                velocity,
                filter);
            _world.Seal();

            _entities = new Entity[population];
            for (int i = 0; i < population; i++)
            {
                if (!_world.TryCreateInitialized(
                    matching,
                    positionBinding,
                    new BenchmarkPosition { X = i, Y = -i },
                    out _entities[i]))
                {
                    throw new InvalidOperationException("Filtered-query benchmark fixture exhausted unexpectedly.");
                }
            }
            if (_plan.MatchedArchetypeCount != 1 || !_plan.Routes[0].HasRowFilters)
                throw new InvalidOperationException("Filtered-query benchmark compiled an unexpected route set.");
            PrepareDense();
        }

        internal void PrepareDense()
        {
            for (int i = 0; i < _entities.Length; i++)
            {
                if (!_world.TrySetComponentEnabled(_entities[i], _velocityBinding, false))
                    throw new InvalidOperationException("Filtered-query dense preparation lost an entity.");
            }
            _expectedMatches = _entities.Length;
        }

        internal void PrepareAlternating()
        {
            for (int i = 0; i < _entities.Length; i++)
            {
                bool enabled = (_entities[i].Index & 1) != 0;
                if (!_world.TrySetComponentEnabled(_entities[i], _velocityBinding, enabled))
                    throw new InvalidOperationException("Filtered-query alternating preparation lost an entity.");
            }
            _expectedMatches = (_entities.Length + 1) >> 1;
        }

        internal ScenarioMeasurement Execute()
        {
            var visitor = new FilteredPositionVelocityVisitor();
            int visited = _world.VisitReadWriteChunks(_plan, ref visitor);
            if (visited != _expectedMatches)
                throw new InvalidOperationException("Filtered-query benchmark selected the wrong row count.");
            return new ScenarioMeasurement(visited, visitor.Checksum);
        }

        public void Dispose() => _world.Dispose();

        private static TArena CreateArena(ChunkArenaCapacity capacity)
        {
            object arena = typeof(TArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported filtered-query benchmark arena {typeof(TArena).Name}.");
            return (TArena)arena;
        }

        private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
        {
            Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
            return componentIds;
        }

        private struct FilteredPositionVelocityVisitor :
            IComponentChunkReadWriteVisitor<BenchmarkPosition, BenchmarkVelocity>
        {
            internal long Checksum;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            public void Execute(in ReadWriteComponentChunk<BenchmarkPosition, BenchmarkVelocity> chunk)
            {
                long checksum = Checksum;
                for (int row = 0; row < chunk.Count; row++)
                {
                    float x = chunk.ReadComponents[row].X;
                    float y = chunk.ReadComponents[row].Y;
                    chunk.WriteComponents[row] = new BenchmarkVelocity
                    {
                        X = x * 2f,
                        Y = y * 2f,
                    };
                    chunk.SetWriteEnabled(row, false);
                    checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(x));
                }
                Checksum = checksum;
            }
        }
    }

    private readonly struct PackedEntityCandidate
    {
        private const ulong IndexMask = 0x00FF_FFFFUL;
        private const ulong GenerationMask = 0x00FF_FFFFUL;
        private readonly ulong _value;

        private PackedEntityCandidate(ulong value)
        {
            _value = value;
        }

        internal uint Index => (uint)(_value & IndexMask);

        internal uint Generation => (uint)((_value >> 24) & GenerationMask);

        internal ushort World => (ushort)(_value >> 48);

        internal static PackedEntityCandidate Create(ushort world, uint index, uint generation)
        {
            ulong value = index & IndexMask;
            value |= ((ulong)generation & GenerationMask) << 24;
            value |= (ulong)world << 48;
            return new PackedEntityCandidate(value);
        }
    }
}

internal readonly record struct BenchmarkScenario(
    string Name,
    string Category,
    int Scale,
    Func<ScenarioMeasurement> Execute,
    Action? Prepare = null);

internal readonly record struct ScenarioMeasurement(
    long Operations,
    long Checksum,
    StructuralPlaybackDiagnostics Diagnostics = default);

internal sealed class BenchmarkReport
{
    public required string SuiteName { get; init; }
    public required string Generation { get; init; }
    public required string SemanticProfile { get; init; }
    public string? ScenarioFilter { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string Configuration { get; init; }
    public required int ProcessorCount { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required string OperatingSystem { get; init; }
    public required int EntitySizeBytes { get; init; }
    public required int ComponentIdSizeBytes { get; init; }
    public required int ComponentMetadataSizeBytes { get; init; }
    public required int ChangeVersionSizeBytes { get; init; }
    public required int ChangeCursorSizeBytes { get; init; }
    public required int ArchetypeHandleSizeBytes { get; init; }
    public required int ArchetypeTransitionSizeBytes { get; init; }
    public required int DeferredEntitySizeBytes { get; init; }
    public required int StructuralCommandRecordSizeBytes { get; init; }
    public required int StructuralPlaybackResultSizeBytes { get; init; }
    public required long StructuralCommandReservedBytes { get; init; }
    public required long PublicLookupRouteBytes { get; init; }
    public required long ExactChangedRowsMetadataBytes { get; init; }
    public required int SchemaIdSizeBytes { get; init; }
    public required long EstimatedEntityPoolMetadataBytesPerSlot { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required IReadOnlyList<LayoutPlanFact> LayoutPlans { get; init; }
    public required IReadOnlyList<ArenaFact> ArenaFacts { get; init; }
    public required IReadOnlyList<ArchetypeFact> ArchetypeFacts { get; init; }
    public required IReadOnlyList<BenchmarkResult> Results { get; init; }
}

internal static class LayoutReportBuilder
{
    private const int ExpectedPopulation = 100_000;

    internal static IReadOnlyList<LayoutPlanFact> Create()
    {
        ComponentSchema schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
        (string Name, ComponentId[] Signature)[] profiles =
        [
            ("Narrow", CreateSortedSignature(
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkPosition>())),
            ("Normal", CreateNormalSignature()),
            ("Wide", CreateSortedSignature(
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkDisabled>(),
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkPosition>(),
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>(),
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkWide>())),
        ];
        ChunkLayoutOptions[] candidates =
        [
            ChunkLayoutOptions.KiB16,
            ChunkLayoutOptions.KiB32,
            ChunkLayoutOptions.KiB64,
        ];
        var facts = new List<LayoutPlanFact>(profiles.Length * candidates.Length);
        foreach ((string profileName, ComponentId[] signature) in profiles)
        {
            foreach (ChunkLayoutOptions options in candidates)
            {
                var columns = new ComponentColumnLayout[signature.Length];
                ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(schema, signature, options, columns);
                var capacity = new ArchetypeCapacityPlan(layout, ExpectedPopulation);
                var columnFacts = new LayoutColumnFact[columns.Length];
                for (int i = 0; i < columns.Length; i++)
                {
                    ComponentColumnLayout column = columns[i];
                    columnFacts[i] = new LayoutColumnFact
                    {
                        ComponentId = column.ComponentId.Value,
                        CanonicalKey = schema.GetCanonicalKey(column.ComponentId),
                        Flags = column.Flags.ToString(),
                        ElementSizeBytes = column.ElementSizeBytes,
                        EffectiveAlignmentBytes = column.EffectiveAlignmentBytes,
                        DataOffsetBytes = column.DataOffsetBytes,
                        DataBytes = column.DataBytes,
                        EnableBitsOffsetBytes = column.EnableBitsOffsetBytes,
                        EnableBitsBytes = column.EnableBitsBytes,
                    };
                }

                facts.Add(new LayoutPlanFact
                {
                    Profile = profileName,
                    ChunkSizeBytes = options.ChunkSizeBytes,
                    ReservedHeaderBytes = options.ReservedHeaderBytes,
                    MinimumColumnAlignmentBytes = options.MinimumColumnAlignmentBytes,
                    RequiredChunkAlignmentBytes = layout.RequiredChunkAlignmentBytes,
                    RowCapacity = layout.RowCapacity,
                    FixedRowBytes = layout.FixedRowBytes,
                    BytesUsed = layout.BytesUsed,
                    AlignmentPaddingBytes = layout.AlignmentPaddingBytes,
                    TailUnusedBytes = layout.TailUnusedBytes,
                    EnableBitsetBytes = layout.EnableBitsetBytes,
                    ExpectedPopulation = ExpectedPopulation,
                    RequiredChunkCount = capacity.RequiredChunkCount,
                    ReservedChunkBytes = capacity.ReservedChunkBytes,
                    UnusedRowSlots = capacity.UnusedRowSlots,
                    Columns = columnFacts,
                });
            }
        }

        return facts;
    }

    internal static ComponentId[] CreateNormalSignature() => CreateSortedSignature(
        UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkDisabled>(),
        UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkPosition>(),
        UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>());

    internal static void CreateNormalLayout(
        ChunkLayoutOptions options,
        out ChunkLayoutPlan layout,
        out ComponentColumnLayout[] columns)
    {
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        ComponentId[] signature = CreateNormalSignature();
        columns = new ComponentColumnLayout[signature.Length];
        layout = ChunkLayoutPlanner.Plan(schema, signature, options, columns);
    }

    internal static ComponentColumnLayout FindColumn<T>(ComponentColumnLayout[] columns)
        where T : unmanaged, IComponent<T>
    {
        ComponentId componentId = BenchmarkSchema.GetComponentId<T>();
        for (int i = 0; i < columns.Length; i++)
        {
            if (columns[i].ComponentId == componentId)
                return columns[i];
        }

        throw new InvalidOperationException($"Layout does not contain {typeof(T).Name}.");
    }

    private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
    {
        Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
        return componentIds;
    }
}

internal static class ArenaFactBuilder
{
    internal static IReadOnlyList<ArenaFact> Create()
    {
        ChunkLayoutOptions[] candidates =
        [
            ChunkLayoutOptions.KiB16,
            ChunkLayoutOptions.KiB32,
            ChunkLayoutOptions.KiB64,
        ];
        var facts = new List<ArenaFact>(candidates.Length * 2);
        foreach (ChunkLayoutOptions options in candidates)
        {
            var capacity = new ChunkArenaCapacity(
                options.ChunkSizeBytes,
                options.MinimumColumnAlignmentBytes,
                maximumChunks: 8);
            using (var native = new NativeChunkArena(capacity))
                facts.Add(CreateFact(native.GetSnapshot(), native.BaseAddress, capacity.TotalPayloadBytes));
            using (var pinned = new PinnedChunkArena(capacity))
            {
                facts.Add(CreateFact(
                    pinned.GetSnapshot(),
                    pinned.BaseAddress,
                    capacity.TotalPayloadBytes + capacity.ChunkAlignmentBytes - 1L));
            }
        }

        return facts;
    }

    private static ArenaFact CreateFact(ChunkArenaSnapshot snapshot, nuint baseAddress, long backingAllocationBytes) => new()
    {
        MemoryKind = snapshot.MemoryKind.ToString(),
        ChunkSizeBytes = snapshot.Capacity.ChunkSizeBytes,
        ChunkAlignmentBytes = snapshot.Capacity.ChunkAlignmentBytes,
        MaximumChunks = snapshot.Capacity.MaximumChunks,
        PayloadBytes = snapshot.Capacity.TotalPayloadBytes,
        BackingAllocationBytes = backingAllocationBytes,
        EstimatedSlotMetadataBytes = snapshot.Capacity.EstimatedSlotMetadataBytes,
        BaseAddressAligned = baseAddress % (nuint)snapshot.Capacity.ChunkAlignmentBytes == 0,
    };
}

internal static class ArchetypeBenchmarkFactory
{
    internal static ArchetypeChunkStore<TArena> CreateStore<TArena>(
        int population,
        out ChunkLayoutPlan layout,
        out ComponentColumnLayout[] columns)
        where TArena : class, IChunkArenaBackend
    {
        LayoutReportBuilder.CreateNormalLayout(ChunkLayoutOptions.KiB32, out layout, out columns);
        var capacityPlan = new ArchetypeCapacityPlan(layout, population);
        ChunkArenaCapacity capacity = ChunkArenaCapacity.FromLayout(layout, capacityPlan.RequiredChunkCount);
        object arena = typeof(TArena) == typeof(NativeChunkArena)
            ? new NativeChunkArena(capacity)
            : typeof(TArena) == typeof(PinnedChunkArena)
                ? new PinnedChunkArena(capacity)
                : throw new InvalidOperationException($"Unsupported benchmark arena {typeof(TArena).Name}.");
        return new ArchetypeChunkStore<TArena>((TArena)arena, layout, columns);
    }
}

internal static class ArchetypeFactBuilder
{
    internal static IReadOnlyList<ArchetypeFact> Create()
    {
        const int population = 100_000;
        return
        [
            Create<NativeChunkArena>(population),
            Create<PinnedChunkArena>(population),
        ];
    }

    private static ArchetypeFact Create<TArena>(int population)
        where TArena : class, IChunkArenaBackend
    {
        using ArchetypeChunkStore<TArena> store = ArchetypeBenchmarkFactory.CreateStore<TArena>(population, out _, out _);
        ArchetypeChunkSnapshot snapshot = store.GetSnapshot();
        return new ArchetypeFact
        {
            MemoryKind = store.Arena.GetSnapshot().MemoryKind.ToString(),
            ExpectedPopulation = population,
            RowCapacity = snapshot.RowCapacity,
            MaximumChunks = snapshot.MaximumChunks,
            MaximumRows = snapshot.MaximumRows,
            ChunkPayloadBytes = store.Arena.Capacity.TotalPayloadBytes,
            ChunkRecordSizeBytes = snapshot.ChunkRecordSizeBytes,
            ChunkMetadataBytes = snapshot.ChunkMetadataBytes,
            ChangeVersionMetadataBytes = snapshot.ChangeVersionMetadataBytes,
            SparseRowChangeVersionMetadataBytes = snapshot.SparseRowChangeVersionMetadataBytes,
            TotalMetadataBytes = snapshot.TotalMetadataBytes,
            ChunkRowSizeBytes = Marshal.SizeOf<ChunkRow>(),
        };
    }
}

internal sealed class ArchetypeFact
{
    public required string MemoryKind { get; init; }
    public required int ExpectedPopulation { get; init; }
    public required int RowCapacity { get; init; }
    public required int MaximumChunks { get; init; }
    public required long MaximumRows { get; init; }
    public required long ChunkPayloadBytes { get; init; }
    public required int ChunkRecordSizeBytes { get; init; }
    public required long ChunkMetadataBytes { get; init; }
    public required long ChangeVersionMetadataBytes { get; init; }
    public required long SparseRowChangeVersionMetadataBytes { get; init; }
    public required long TotalMetadataBytes { get; init; }
    public required int ChunkRowSizeBytes { get; init; }
}

internal sealed class ArenaFact
{
    public required string MemoryKind { get; init; }
    public required int ChunkSizeBytes { get; init; }
    public required int ChunkAlignmentBytes { get; init; }
    public required int MaximumChunks { get; init; }
    public required long PayloadBytes { get; init; }
    public required long BackingAllocationBytes { get; init; }
    public required long EstimatedSlotMetadataBytes { get; init; }
    public required bool BaseAddressAligned { get; init; }
}

internal sealed class LayoutPlanFact
{
    public required string Profile { get; init; }
    public required int ChunkSizeBytes { get; init; }
    public required int ReservedHeaderBytes { get; init; }
    public required int MinimumColumnAlignmentBytes { get; init; }
    public required int RequiredChunkAlignmentBytes { get; init; }
    public required int RowCapacity { get; init; }
    public required int FixedRowBytes { get; init; }
    public required int BytesUsed { get; init; }
    public required int AlignmentPaddingBytes { get; init; }
    public required int TailUnusedBytes { get; init; }
    public required int EnableBitsetBytes { get; init; }
    public required int ExpectedPopulation { get; init; }
    public required int RequiredChunkCount { get; init; }
    public required long ReservedChunkBytes { get; init; }
    public required long UnusedRowSlots { get; init; }
    public required IReadOnlyList<LayoutColumnFact> Columns { get; init; }
}

internal sealed class LayoutColumnFact
{
    public required ushort ComponentId { get; init; }
    public required string CanonicalKey { get; init; }
    public required string Flags { get; init; }
    public required int ElementSizeBytes { get; init; }
    public required int EffectiveAlignmentBytes { get; init; }
    public required int DataOffsetBytes { get; init; }
    public required int DataBytes { get; init; }
    public required int EnableBitsOffsetBytes { get; init; }
    public required int EnableBitsBytes { get; init; }
}

internal sealed class BenchmarkResult
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required bool Comparable { get; init; }
    public required int Scale { get; init; }
    public required int WarmupCount { get; init; }
    public required int IterationCount { get; init; }
    public required long OperationsPerIteration { get; init; }
    public required double MeanMilliseconds { get; init; }
    public required double MinMilliseconds { get; init; }
    public required double P50Milliseconds { get; init; }
    public required double P95Milliseconds { get; init; }
    public required double P99Milliseconds { get; init; }
    public required double MaxMilliseconds { get; init; }
    public required double MeanNanosecondsPerOperation { get; init; }
    public double P95NanosecondsPerOperation { get; init; }
    public required double OperationsPerSecond { get; init; }
    public required long MeanAllocatedBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required long Checksum { get; init; }
    public int ExactWholeSourceResets { get; init; }
    public int ExactSuffixShrinks { get; init; }
    public int ExactSuffixCommands { get; init; }
    public int ExactSuffixBlocksStreamed { get; init; }
    public int ExactSuffixRowsBulkShrunk { get; init; }
    public int ExactSuffixDonorRelocations { get; init; }
    public int FailedExactSuffixCandidates { get; init; }
}
