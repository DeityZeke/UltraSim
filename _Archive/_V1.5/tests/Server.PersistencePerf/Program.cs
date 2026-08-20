using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using UltraSim.Network;
using UltraSim.Server;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Server.Gameplay;
using UltraSim.Shared.Gameplay;

namespace Server.PersistencePerf
{
    internal static class Program
    {
        private static readonly int[] DefaultPlayerCounts = { 10, 100, 1000 };
        private static readonly int[] DefaultItemCounts = { 10, 100, 1000, 10_000, 100_000 };
        private static readonly int[] DefaultSystemCounts = { 10, 25, 50, 100, 150 };

        private sealed class FakeTransport : INetworkTransport
        {
            public void Start() { }
            public void Stop() { }
            public int PollInbound(Span<InboundDatagram> destination) => 0;
            public void EnqueueOutbound(in OutboundDatagram datagram) { }
            public void FlushOutbound() { }
            public void CompleteInboundConsume() { }
            public void Dispose() { }
        }

        private sealed class TestAuthProvider : IAccountAuthenticationProvider
        {
            public bool TryAuthenticateExistingAccount(
                AccountModel account,
                ReadOnlySpan<byte> passwordHash32,
                ReadOnlySpan<byte> challengeResponse32,
                ReadOnlySpan<byte> challenge16,
                out string errorMessage)
            {
                errorMessage = string.Empty;
                return true;
            }

            public bool TryPrepareNewAccount(
                string username,
                ReadOnlySpan<byte> passwordHash32,
                ReadOnlySpan<byte> challengeResponse32,
                ReadOnlySpan<byte> challenge16,
                out string passwordHash,
                out string passwordSalt,
                out string errorMessage)
            {
                passwordHash = "TEST_HASH";
                passwordSalt = "TEST_SALT";
                errorMessage = string.Empty;
                return true;
            }
        }

        [PersistenceId(49001u)]
        private struct PerfItemStateComponent : ISerializableComponent<PerfItemStateComponent>
        {
            public int TemplateId;
            public int StackCount;
            public int ValueCents;

            public static void Serialize(ref UltraSim.ECS.BitWriter writer, in PerfItemStateComponent value)
            {
                writer.WriteInt32(value.TemplateId);
                writer.WriteInt32(value.StackCount);
                writer.WriteInt32(value.ValueCents);
            }

            public static PerfItemStateComponent Deserialize(ref UltraSim.ECS.BitReader reader, ushort version)
            {
                return new PerfItemStateComponent
                {
                    TemplateId = reader.ReadInt32(),
                    StackCount = reader.ReadInt32(),
                    ValueCents = reader.ReadInt32(),
                };
            }
        }

        [PersistenceId(49002u)]
        private struct PerfItemDurabilityComponent : ISerializableComponent<PerfItemDurabilityComponent>
        {
            public float Durability;
            public ushort Flags;

            public static void Serialize(ref UltraSim.ECS.BitWriter writer, in PerfItemDurabilityComponent value)
            {
                writer.WriteSingle(value.Durability);
                writer.WriteUInt16(value.Flags);
            }

            public static PerfItemDurabilityComponent Deserialize(ref UltraSim.ECS.BitReader reader, ushort version)
            {
                return new PerfItemDurabilityComponent
                {
                    Durability = reader.ReadSingle(),
                    Flags = reader.ReadUInt16(),
                };
            }
        }

        private struct PerfItemTag : IComponent<PerfItemTag> { }

        private readonly record struct HarnessOptions(
            int Iterations,
            int[] PlayerCounts,
            int[] ItemCounts,
            int[] SystemCounts,
            bool RunPlayers,
            bool RunItems,
            bool RunMixed,
            bool RunParallelSave,
            bool RunTickWindow,
            string? OutCsv,
            string? OutJson,
            int TickWindowTicks);

        private readonly record struct Scenario(string Kind, int Players, int Items)
        {
            public string Name => Kind switch
            {
                "players" => $"players-{Players}",
                "items" => $"items-{Items}",
                "tickwindow" => $"tickwindow-p{Players}-i{Items}",
                _ => $"mixed-p{Players}-i{Items}",
            };
        }

        private readonly record struct ParallelSaveScenario(int Players, int Items, int Systems)
        {
            public string Name => $"parallel-p{Players}-i{Items}-s{Systems}";
        }

        private readonly record struct GcDelta(
            long AllocatedBytes,
            int Gen0Collections,
            int Gen1Collections,
            int Gen2Collections);

        private readonly record struct ScenarioMeasurement(
            Scenario Scenario,
            int Iteration,
            double SaveMilliseconds,
            double LoadMilliseconds,
            double AccountSaveMilliseconds,
            double WorldSaveMilliseconds,
            double WorldSaveStructuralFlushMilliseconds,
            double WorldSaveTypeDatabaseMilliseconds,
            double WorldSaveSystemsMilliseconds,
            double WorldSaveEntitiesMilliseconds,
            double WorldSaveManifestMilliseconds,
            double WorldSaveCommitMilliseconds,
            double AccountLoadMilliseconds,
            double WorldLoadMilliseconds,
            double WorldLoadManifestMilliseconds,
            double WorldLoadTypeValidationMilliseconds,
            double WorldLoadSystemsMilliseconds,
            double WorldLoadEntityCreateMilliseconds,
            double WorldLoadHydrateMilliseconds,
            double WorldLoadFixupMilliseconds,
            GcDelta SaveGc,
            GcDelta LoadGc,
            long MetaBytes,
            long TypesBytes,
            long EntitiesIdxBytes,
            long EntitiesBinBytes,
            long AccountsBytes);

        private readonly record struct ParallelSaveMeasurement(
            ParallelSaveScenario Scenario,
            int Iteration,
            double BaselineWorldSaveMilliseconds,
            double PartitionMilliseconds,
            double SerialTotalMilliseconds,
            double SerialItemsMilliseconds,
            double SerialMobilesMilliseconds,
            double SerialSystemsMilliseconds,
            double SystemsParallelTotalMilliseconds,
            double SystemsParallelEntityBranchMilliseconds,
            double SystemsParallelSystemsMilliseconds,
            double SystemsParallelCoordinationOverheadMilliseconds,
            double ParallelTotalMilliseconds,
            double ParallelItemsMilliseconds,
            double ParallelMobilesMilliseconds,
            double ParallelSystemsMilliseconds,
            double ParallelCoordinationOverheadMilliseconds,
            GcDelta BaselineGc,
            GcDelta SerialGc,
            GcDelta SystemsParallelGc,
            GcDelta ParallelGc,
            int ItemEntityCount,
            int MobileEntityCount,
            int SystemFileCount);

        private static int Main(string[] args)
        {
            try
            {
                HarnessOptions options = ParseArgs(args);
                PrintHeader(options);
                Warmup();

                string runAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                var allPerfResults = new List<(Scenario Scenario, IReadOnlyList<ScenarioMeasurement> Measurements)>();
                var allParallelSaveResults = new List<(ParallelSaveScenario Scenario, IReadOnlyList<ParallelSaveMeasurement> Measurements)>();
                var allTickWindowResults = new List<(Scenario Scenario, IReadOnlyList<TickWindowMeasurement> Measurements)>();

                foreach (Scenario scenario in EnumerateScenarios(options))
                {
                    var measurements = new List<ScenarioMeasurement>(options.Iterations);
                    for (int iteration = 1; iteration <= options.Iterations; iteration++)
                    {
                        ScenarioMeasurement measurement = RunScenario(scenario, iteration);
                        measurements.Add(measurement);
                        PrintMeasurement(measurement);
                    }

                    PrintSummary(scenario, measurements);
                    allPerfResults.Add((scenario, measurements));
                }

                if (options.RunParallelSave)
                {
                    foreach (ParallelSaveScenario scenario in EnumerateParallelSaveScenarios(options))
                    {
                        var measurements = new List<ParallelSaveMeasurement>(options.Iterations);
                        for (int iteration = 1; iteration <= options.Iterations; iteration++)
                        {
                            ParallelSaveMeasurement measurement = RunParallelSaveScenario(scenario, iteration);
                            measurements.Add(measurement);
                            PrintParallelSaveMeasurement(measurement);
                        }

                        PrintParallelSaveSummary(scenario, measurements);
                        allParallelSaveResults.Add((scenario, measurements));
                    }
                }

                if (options.RunTickWindow)
                {
                    foreach (Scenario scenario in EnumerateTickWindowScenarios(options))
                    {
                        var measurements = new List<TickWindowMeasurement>(options.Iterations);
                        for (int iteration = 1; iteration <= options.Iterations; iteration++)
                        {
                            TickWindowMeasurement measurement = RunTickWindowScenario(scenario, iteration, options.TickWindowTicks);
                            measurements.Add(measurement);
                            PrintTickWindowMeasurement(measurement);
                        }

                        PrintTickWindowSummary(scenario, measurements);
                        allTickWindowResults.Add((scenario, measurements));
                    }
                }

                if (options.RunParallelSave && (options.OutCsv is not null || options.OutJson is not null))
                    Console.WriteLine("Note: CSV/JSON export currently includes the existing perf and tick-window runs only; parallel-save results stay in console output.");

                if (options.OutCsv is not null)
                    WriteCsv(options.OutCsv, runAt, allPerfResults, allTickWindowResults);

                if (options.OutJson is not null)
                    WriteJson(options.OutJson, runAt, options, allPerfResults, allTickWindowResults);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static HarnessOptions ParseArgs(string[] args)
        {
            int iterations = 1;
            int[] playerCounts = DefaultPlayerCounts;
            int[] itemCounts = DefaultItemCounts;
            int[] systemCounts = DefaultSystemCounts;
            bool runPlayers = true;
            bool runItems = true;
            bool runMixed = true;
            bool runParallelSave = false;
            bool runTickWindow = false;
            string? outCsv = null;
            string? outJson = null;
            int tickWindowTicks = 100;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--iterations":
                        iterations = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--players":
                        playerCounts = ParseCountList(args[++i]);
                        break;
                    case "--items":
                        itemCounts = ParseCountList(args[++i]);
                        break;
                    case "--systems":
                        systemCounts = ParseCountList(args[++i]);
                        break;
                    case "--tick-window-ticks":
                        tickWindowTicks = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--out-csv":
                        outCsv = args[++i];
                        break;
                    case "--out-json":
                        outJson = args[++i];
                        break;
                    case "--modes":
                        runPlayers = false;
                        runItems = false;
                        runMixed = false;
                        runTickWindow = false;
                        foreach (string mode in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            switch (mode.ToLowerInvariant())
                            {
                                case "players":
                                    runPlayers = true;
                                    break;
                                case "items":
                                    runItems = true;
                                    break;
                                case "mixed":
                                    runMixed = true;
                                    break;
                                case "parallelsave":
                                    runParallelSave = true;
                                    break;
                                case "tickwindow":
                                    runTickWindow = true;
                                    break;
                                default:
                                    throw new ArgumentException($"Unknown mode '{mode}'. Expected players, items, mixed, parallelsave, or tickwindow.");
                            }
                        }
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{arg}'.");
                }
            }

            if (iterations <= 0)
                throw new ArgumentOutOfRangeException(nameof(iterations));

            if (tickWindowTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickWindowTicks));

            return new HarnessOptions(iterations, playerCounts, itemCounts, systemCounts, runPlayers, runItems, runMixed,
                runParallelSave, runTickWindow, outCsv, outJson, tickWindowTicks);
        }

        private static int[] ParseCountList(string raw)
        {
            string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
            return result;
        }

        private static void PrintHeader(HarnessOptions options)
        {
            Console.WriteLine("Persistence performance harness");
            Console.WriteLine($"Iterations: {options.Iterations}");
            Console.WriteLine($"Players: {string.Join(", ", options.PlayerCounts)}");
            Console.WriteLine($"Items: {string.Join(", ", options.ItemCounts)}");
            if (options.RunParallelSave)
                Console.WriteLine($"Systems: {string.Join(", ", options.SystemCounts)}");
            string modes = string.Concat(
                options.RunPlayers ? "players " : string.Empty,
                options.RunItems ? "items " : string.Empty,
                options.RunMixed ? "mixed " : string.Empty,
                options.RunParallelSave ? "parallelsave " : string.Empty,
                options.RunTickWindow ? "tickwindow" : string.Empty).Trim();
            Console.WriteLine($"Modes: {modes}");
            if (options.RunTickWindow)
                Console.WriteLine($"Tick-window ticks (before + after): {options.TickWindowTicks}");
            if (options.OutCsv is not null)
                Console.WriteLine($"CSV output: {options.OutCsv}");
            if (options.OutJson is not null)
                Console.WriteLine($"JSON output: {options.OutJson}");
            Console.WriteLine($"CPU count: {Environment.ProcessorCount}");
            Console.WriteLine();
        }

        private static void Warmup()
        {
            Console.WriteLine("Warmup: players=1 items=1 (perf + tick-window paths)");
            _ = RunScenario(new Scenario("mixed", 1, 1), iteration: 0);
            _ = RunParallelSaveScenario(new ParallelSaveScenario(1, 1, 10), iteration: 0);
            _ = RunTickWindowScenario(new Scenario("tickwindow", 1, 1), iteration: 0, tickWindowTicks: 10);
            Console.WriteLine();
        }

        private static IEnumerable<Scenario> EnumerateScenarios(HarnessOptions options)
        {
            if (options.RunPlayers)
            {
                for (int i = 0; i < options.PlayerCounts.Length; i++)
                    yield return new Scenario("players", options.PlayerCounts[i], 0);
            }

            if (options.RunItems)
            {
                for (int i = 0; i < options.ItemCounts.Length; i++)
                    yield return new Scenario("items", 0, options.ItemCounts[i]);
            }

            if (options.RunMixed)
            {
                for (int i = 0; i < options.PlayerCounts.Length; i++)
                {
                    for (int j = 0; j < options.ItemCounts.Length; j++)
                        yield return new Scenario("mixed", options.PlayerCounts[i], options.ItemCounts[j]);
                }
            }
        }

        private static IEnumerable<ParallelSaveScenario> EnumerateParallelSaveScenarios(HarnessOptions options)
        {
            for (int i = 0; i < options.PlayerCounts.Length; i++)
            {
                for (int j = 0; j < options.ItemCounts.Length; j++)
                {
                    for (int k = 0; k < options.SystemCounts.Length; k++)
                        yield return new ParallelSaveScenario(options.PlayerCounts[i], options.ItemCounts[j], options.SystemCounts[k]);
                }
            }
        }

        private static ScenarioMeasurement RunScenario(Scenario scenario, int iteration)
        {
            string root = Path.Combine(Path.GetTempPath(), $"persist_perf_{scenario.Name}_{iteration}_{Guid.NewGuid():N}");
            string saveRoot = Path.Combine(root, "Saves");
            string accountsRoot = Path.Combine(root, "accounts");

            try
            {
                ResetStatics();
                EnsurePerfComponentsRegistered();
                RuntimeHarnessContext saveContext = CreateRuntime(root);
                ServerRuntime saveRuntime = saveContext.Runtime;
                saveRuntime.Start();

                PopulateRuntime(saveRuntime, scenario.Players, scenario.Items);

                GcDelta saveGc;
                double saveMs = MeasureMilliseconds(() => saveRuntime.Save(), out saveGc);
                saveRuntime.Stop();

                ResetStatics();
                EnsurePerfComponentsRegistered();
                RuntimeHarnessContext loadContext = CreateRuntime(root);
                ServerRuntime loadRuntime = loadContext.Runtime;
                GcDelta loadGc;
                double loadMs = MeasureMilliseconds(loadRuntime.Start, out loadGc);

                VerifyLoadedCounts(loadRuntime, scenario.Players, scenario.Items);
                loadRuntime.Stop();

                return new ScenarioMeasurement(
                    scenario,
                    iteration,
                    saveMs,
                    loadMs,
                    saveContext.AccountCoordinator.LastSaveTiming.TotalDuration.TotalMilliseconds,
                    saveContext.WorldCoordinator.LastSaveTiming.TotalDuration.TotalMilliseconds,
                    saveContext.WorldCoordinator.LastSaveTiming.StructuralFlushDuration.TotalMilliseconds,
                    saveContext.WorldCoordinator.LastSaveTiming.TypeDatabaseWriteDuration.TotalMilliseconds,
                    saveContext.WorldCoordinator.LastSaveTiming.SystemsWriteDuration.TotalMilliseconds,
                    saveContext.WorldCoordinator.LastSaveTiming.EntitiesWriteDuration.TotalMilliseconds,
                    saveContext.WorldCoordinator.LastSaveTiming.ManifestWriteDuration.TotalMilliseconds,
                    saveContext.WorldCoordinator.LastSaveTiming.CommitDuration.TotalMilliseconds,
                    loadContext.AccountCoordinator.LastLoadTiming.TotalDuration.TotalMilliseconds,
                    loadContext.WorldCoordinator.LastLoadTiming.TotalDuration.TotalMilliseconds,
                    loadContext.WorldCoordinator.LastLoadTiming.ManifestReadDuration.TotalMilliseconds,
                    loadContext.WorldCoordinator.LastLoadTiming.TypeValidationDuration.TotalMilliseconds,
                    loadContext.WorldCoordinator.LastLoadTiming.SystemsLoadDuration.TotalMilliseconds,
                    loadContext.WorldCoordinator.LastLoadTiming.EntityCreateDuration.TotalMilliseconds,
                    loadContext.WorldCoordinator.LastLoadTiming.HydrateDuration.TotalMilliseconds,
                    loadContext.WorldCoordinator.LastLoadTiming.FixupDuration.TotalMilliseconds,
                    saveGc,
                    loadGc,
                    SafeFileLength(Path.Combine(saveRoot, "meta.bin")),
                    SafeFileLength(Path.Combine(saveRoot, "types.tdb")),
                    SafeFileLength(Path.Combine(saveRoot, "Entities", "entities.idx")),
                    SafeFileLength(Path.Combine(saveRoot, "Entities", "entities.bin")),
                    SafeFileLength(Path.Combine(accountsRoot, "accounts.dat")));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        private static ParallelSaveMeasurement RunParallelSaveScenario(ParallelSaveScenario scenario, int iteration)
        {
            string root = Path.Combine(Path.GetTempPath(), $"persist_parallel_{scenario.Name}_{iteration}_{Guid.NewGuid():N}");

            try
            {
                ResetStatics();
                EnsurePerfComponentsRegistered();
                PersistenceTypeRegistry registry = BuildParallelSaveRegistry(scenario.Systems);
                World world = CreateParallelSaveWorld(scenario.Players, scenario.Items, scenario.Systems);

                double baselineWorldSaveMs = MeasureMilliseconds(
                    () => RunBaselineFullWorldSave(world, registry, Path.Combine(root, "baseline_full")),
                    out GcDelta baselineGc);

                double partitionMs = 0;
                IReadOnlyList<PersistenceArchetypeView> itemArchetypes;
                IReadOnlyList<PersistenceArchetypeView> mobileArchetypes;
                int itemEntityCount;
                int mobileEntityCount;
                using (MeasureScope.Start(milliseconds => partitionMs = milliseconds))
                {
                    world.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
                    PartitionPersistentArchetypes(
                        world,
                        out itemArchetypes,
                        out mobileArchetypes,
                        out itemEntityCount,
                        out mobileEntityCount);
                }

                if (itemEntityCount != scenario.Items)
                    throw new InvalidOperationException($"Parallel save item partition mismatch. Expected {scenario.Items}, got {itemEntityCount}.");
                if (mobileEntityCount != scenario.Players)
                    throw new InvalidOperationException($"Parallel save mobile partition mismatch. Expected {scenario.Players}, got {mobileEntityCount}.");

                PartitionSaveResult serialResult = RunPartitionedSave(
                    registry,
                    world,
                    root,
                    itemArchetypes,
                    mobileArchetypes,
                    scenario.Systems,
                    mode: PartitionSaveMode.Serial,
                    out GcDelta serialGc);

                PartitionSaveResult systemsParallelResult = RunPartitionedSave(
                    registry,
                    world,
                    root,
                    itemArchetypes,
                    mobileArchetypes,
                    scenario.Systems,
                    mode: PartitionSaveMode.SystemsParallelOnly,
                    out GcDelta systemsParallelGc);

                PartitionSaveResult parallelResult = RunPartitionedSave(
                    registry,
                    world,
                    root,
                    itemArchetypes,
                    mobileArchetypes,
                    scenario.Systems,
                    mode: PartitionSaveMode.AllPartitionsParallel,
                    out GcDelta parallelGc);

                return new ParallelSaveMeasurement(
                    scenario,
                    iteration,
                    baselineWorldSaveMs,
                    partitionMs,
                    serialResult.TotalMilliseconds,
                    serialResult.ItemsMilliseconds,
                    serialResult.MobilesMilliseconds,
                    serialResult.SystemsMilliseconds,
                    systemsParallelResult.TotalMilliseconds,
                    systemsParallelResult.EntityBranchMilliseconds,
                    systemsParallelResult.SystemsMilliseconds,
                    systemsParallelResult.CoordinationOverheadMilliseconds,
                    parallelResult.TotalMilliseconds,
                    parallelResult.ItemsMilliseconds,
                    parallelResult.MobilesMilliseconds,
                    parallelResult.SystemsMilliseconds,
                    parallelResult.CoordinationOverheadMilliseconds,
                    baselineGc,
                    serialGc,
                    systemsParallelGc,
                    parallelGc,
                    itemEntityCount,
                    mobileEntityCount,
                    parallelResult.SystemFileCount);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        private static void PartitionPersistentArchetypes(
            World world,
            out IReadOnlyList<PersistenceArchetypeView> itemArchetypes,
            out IReadOnlyList<PersistenceArchetypeView> mobileArchetypes,
            out int itemEntityCount,
            out int mobileEntityCount)
        {
            var worldView = new WorldPersistenceView(world);
            IReadOnlyList<PersistenceArchetypeView> persistentArchetypes = worldView.GetPersistentArchetypes();
            ComponentId itemTagId = ComponentManager.GetId<PerfItemTag>();
            ComponentId playerMarkerId = ComponentManager.GetId<PlayerMarker>();
            var items = new List<PersistenceArchetypeView>(persistentArchetypes.Count);
            var mobiles = new List<PersistenceArchetypeView>(persistentArchetypes.Count);

            itemEntityCount = 0;
            mobileEntityCount = 0;

            for (int i = 0; i < persistentArchetypes.Count; i++)
            {
                PersistenceArchetypeView archetype = persistentArchetypes[i];
                if (archetype.HasComponent(itemTagId))
                {
                    items.Add(archetype);
                    itemEntityCount += archetype.EntityCount;
                    continue;
                }

                if (archetype.HasComponent(playerMarkerId))
                {
                    mobiles.Add(archetype);
                    mobileEntityCount += archetype.EntityCount;
                }
            }

            itemArchetypes = items;
            mobileArchetypes = mobiles;
        }

        private enum PartitionSaveMode
        {
            Serial,
            SystemsParallelOnly,
            AllPartitionsParallel,
        }

        private static PartitionSaveResult RunPartitionedSave(
            PersistenceTypeRegistry registry,
            World world,
            string root,
            IReadOnlyList<PersistenceArchetypeView> itemArchetypes,
            IReadOnlyList<PersistenceArchetypeView> mobileArchetypes,
            int expectedSystemCount,
            PartitionSaveMode mode,
            out GcDelta gcDelta)
        {
            string modeRoot = Path.Combine(root, mode switch
            {
                PartitionSaveMode.Serial => "serial_partition",
                PartitionSaveMode.SystemsParallelOnly => "systems_parallel_partition",
                _ => "parallel_partition",
            });
            string itemsRoot = Path.Combine(modeRoot, "Items");
            string mobilesRoot = Path.Combine(modeRoot, "Mobiles");
            string systemsRoot = Path.Combine(modeRoot, "SystemSubset");

            void ValidateSystemCount()
            {
                int count = Directory.Exists(Path.Combine(systemsRoot, "Systems"))
                    ? Directory.GetFiles(Path.Combine(systemsRoot, "Systems"), "*.bin").Length
                    : 0;
                if (count != expectedSystemCount)
                {
                    throw new InvalidOperationException(
                        $"System file count mismatch for {mode} partition save. Expected {expectedSystemCount}, got {count}.");
                }
            }

            double itemsMs = 0;
            double mobilesMs = 0;
            double systemsMs = 0;
            double entityBranchMs = 0;
            int systemFileCount = 0;

            double totalMs = MeasureMilliseconds(() =>
            {
                if (Directory.Exists(modeRoot))
                    Directory.Delete(modeRoot, recursive: true);

                if (mode == PartitionSaveMode.Serial)
                {
                    using (MeasureScope.Start(milliseconds => itemsMs = milliseconds))
                    {
                        EntityPersistenceWriter.WriteArchetypes(itemArchetypes, new SaveSetPaths(itemsRoot), registry);
                    }

                    using (MeasureScope.Start(milliseconds => mobilesMs = milliseconds))
                    {
                        EntityPersistenceWriter.WriteArchetypes(mobileArchetypes, new SaveSetPaths(mobilesRoot), registry);
                    }

                    using (MeasureScope.Start(milliseconds => systemsMs = milliseconds))
                    {
                        systemFileCount = SystemPersistenceSerializer.WriteAll(world, new SaveSetPaths(systemsRoot), registry);
                    }
                }
                else if (mode == PartitionSaveMode.SystemsParallelOnly)
                {
                    Task entityTask = Task.Run(() =>
                    {
                        using var branchScope = MeasureScope.Start(milliseconds => entityBranchMs = milliseconds);
                        using (MeasureScope.Start(milliseconds => itemsMs = milliseconds))
                        {
                            EntityPersistenceWriter.WriteArchetypes(itemArchetypes, new SaveSetPaths(itemsRoot), registry);
                        }

                        using (MeasureScope.Start(milliseconds => mobilesMs = milliseconds))
                        {
                            EntityPersistenceWriter.WriteArchetypes(mobileArchetypes, new SaveSetPaths(mobilesRoot), registry);
                        }
                    });

                    Task systemTask = Task.Run(() =>
                    {
                        using var scope = MeasureScope.Start(milliseconds => systemsMs = milliseconds);
                        systemFileCount = SystemPersistenceSerializer.WriteAll(world, new SaveSetPaths(systemsRoot), registry);
                    });

                    Task.WaitAll(entityTask, systemTask);
                }
                else
                {
                    Task itemTask = Task.Run(() =>
                    {
                        using var scope = MeasureScope.Start(milliseconds => itemsMs = milliseconds);
                        EntityPersistenceWriter.WriteArchetypes(itemArchetypes, new SaveSetPaths(itemsRoot), registry);
                    });

                    Task mobileTask = Task.Run(() =>
                    {
                        using var scope = MeasureScope.Start(milliseconds => mobilesMs = milliseconds);
                        EntityPersistenceWriter.WriteArchetypes(mobileArchetypes, new SaveSetPaths(mobilesRoot), registry);
                    });

                    Task systemTask = Task.Run(() =>
                    {
                        using var scope = MeasureScope.Start(milliseconds => systemsMs = milliseconds);
                        systemFileCount = SystemPersistenceSerializer.WriteAll(world, new SaveSetPaths(systemsRoot), registry);
                    });

                    Task.WaitAll(itemTask, mobileTask, systemTask);
                }

                ValidateSystemCount();
            }, out gcDelta);

            return new PartitionSaveResult(
                totalMs,
                itemsMs,
                mobilesMs,
                systemsMs,
                entityBranchMs,
                mode == PartitionSaveMode.Serial ? 0 : Math.Max(0, totalMs - Math.Max(
                    systemsMs,
                    mode == PartitionSaveMode.SystemsParallelOnly
                        ? entityBranchMs
                        : Math.Max(itemsMs, mobilesMs))),
                systemFileCount);
        }

        private static double MeasureMilliseconds(Action action, out GcDelta gcDelta)
        {
            long allocatedBefore = GC.GetTotalAllocatedBytes(true);
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);

            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();

            gcDelta = new GcDelta(
                GC.GetTotalAllocatedBytes(false) - allocatedBefore,
                GC.CollectionCount(0) - gen0Before,
                GC.CollectionCount(1) - gen1Before,
                GC.CollectionCount(2) - gen2Before);
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private readonly record struct PartitionSaveResult(
            double TotalMilliseconds,
            double ItemsMilliseconds,
            double MobilesMilliseconds,
            double SystemsMilliseconds,
            double EntityBranchMilliseconds,
            double CoordinationOverheadMilliseconds,
            int SystemFileCount);

        private sealed class MeasureScope : IDisposable
        {
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private readonly Action<double> _setter;

            private MeasureScope(Action<double> setter)
            {
                _setter = setter;
            }

            public static MeasureScope Start(Action<double> setter) => new(setter);

            public void Dispose()
            {
                _stopwatch.Stop();
                _setter(_stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private static long SafeFileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

        private static void PopulateRuntime(ServerRuntime runtime, int playerCount, int itemCount)
        {
            if (playerCount > 0)
                CreatePlayers(runtime, playerCount);

            if (itemCount > 0)
                CreateItems(runtime, itemCount);
        }

        private static void CreatePlayers(ServerRuntime runtime, int count)
        {
            byte[] payload = new byte[32];
            byte[] challenge = new byte[16];

            for (int i = 0; i < count; i++)
            {
                string username = $"user{i + 1}";
                string characterName = $"Char{i + 1}";
                ushort prefabId = (i & 1) == 0 ? PlayerArchetypeIds.Default : PlayerArchetypeIds.DefaultFemale;
                string remoteIp = $"10.0.{(i / 250) % 250}.{(i % 250) + 1}";

                if (!runtime.Accounts.TryLogin(username, payload, payload, challenge, remoteIp, out AccountModel? account, out _, out string loginError))
                    throw new InvalidOperationException($"TryLogin failed for {username}: {loginError}");
                if (account is null)
                    throw new InvalidOperationException($"TryLogin returned no account for {username}.");
                if (!runtime.Accounts.TryCreateCharacter(account, characterName, prefabId, out _, out CharacterModel? character, out string createError))
                    throw new InvalidOperationException($"TryCreateCharacter failed for {username}: {createError}");
                if (character is null)
                    throw new InvalidOperationException($"TryCreateCharacter returned no character for {username}.");

                Entity entity = runtime.PlayerWorld.BindSession(
                    sessionId: i + 1,
                    connectionId: i + 1,
                    requestedPrefabId: character.ArchetypeId,
                    playerDisplayName: character.Name,
                    characterIdentity: new PlayerCharacterComponent
                    {
                        CharacterId = character.CharacterId,
                        SlotIndex = character.SlotIndex,
                    },
                    spawnSnapshot: new PlayerSpawnSnapshot(character.X, character.Y, character.Z, character.YawRadians));

                if (!runtime.PlayerWorld.World.TryGetComponent(entity, out PlayerState state))
                    throw new InvalidOperationException("Expected PlayerState on created player.");
                state.X = i * 1.25f;
                state.Y = (i % 7) * 0.5f;
                state.Z = -i * 0.75f;
                state.YawRadians = (i % 360) * 0.017453292f;
                if (!runtime.PlayerWorld.World.TrySetComponent(entity, state))
                    throw new InvalidOperationException("Failed to write PlayerState.");
            }
        }

        private static void CreateItems(ServerRuntime runtime, int count)
        {
            ComponentId persistentTagId = ComponentManager.GetId<PersistentTag>();
            ComponentId prefabId = ComponentManager.GetId<PrefabIdComponent>();
            ComponentId itemTagId = ComponentManager.GetId<PerfItemTag>();
            ComponentId itemStateId = ComponentManager.GetId<PerfItemStateComponent>();
            ComponentId durabilityId = ComponentManager.GetId<PerfItemDurabilityComponent>();
            ArchetypeSignature signature = new ArchetypeSignature(new[] { persistentTagId, prefabId, itemTagId, itemStateId, durabilityId });

            for (int i = 0; i < count; i++)
                runtime.PlayerWorld.World.EnqueueCreateDefault(signature);

            runtime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

            int itemIndex = 0;
            runtime.PlayerWorld.World.Query<PerfItemStateComponent, PerfItemDurabilityComponent, PrefabIdComponent>(
                (Entity entity, ref PerfItemStateComponent itemState, ref PerfItemDurabilityComponent durability, ref PrefabIdComponent prefab) =>
                {
                    prefab.PrefabId = (ushort)(5000 + (itemIndex % 64));
                    itemState = new PerfItemStateComponent
                    {
                        TemplateId = 100000 + itemIndex,
                        StackCount = 1 + (itemIndex % 20),
                        ValueCents = 25 + (itemIndex % 5000),
                    };
                    durability = new PerfItemDurabilityComponent
                    {
                        Durability = 0.25f + ((itemIndex % 100) / 100f),
                        Flags = (ushort)(itemIndex & 0xF),
                    };
                    itemIndex++;
                });

            if (itemIndex < count)
                throw new InvalidOperationException($"Expected at least {count} item entities, found {itemIndex}.");
        }

        private static PersistenceTypeRegistry BuildParallelSaveRegistry(int persistentSystemCount)
        {
            var registry = new PersistenceTypeRegistry();
            registry.Register<PlayerMarker>(PersistenceKind.Component);
            registry.Register<PlayerState>(PersistenceKind.Component);
            registry.Register<PrefabIdComponent>(PersistenceKind.Component);
            registry.Register<PersistentTag>(PersistenceKind.Component);
            registry.Register<PlayerNameComponent>(PersistenceKind.Component);
            registry.Register<PlayerCharacterComponent>(PersistenceKind.Component);
            registry.Register<PerfItemStateComponent>(PersistenceKind.Component);
            registry.Register<PerfItemDurabilityComponent>(PersistenceKind.Component);

            for (int i = 0; i < persistentSystemCount; i++)
                registry.Register(PerfPersistentSystemCatalog.Types[i], PersistenceKind.System);

            registry.Seal();
            return registry;
        }

        private static World CreateParallelSaveWorld(int playerCount, int itemCount, int persistentSystemCount)
        {
            var world = new World();
            RegisterPerfSystems(world, persistentSystemCount);
            world.Initialize();

            if (playerCount > 0)
                CreateParallelPlayers(world, playerCount);

            if (itemCount > 0)
                CreateParallelItems(world, itemCount);

            return world;
        }

        private static void CreateParallelPlayers(World world, int count)
        {
            ComponentId persistentTagId = ComponentManager.GetId<PersistentTag>();
            ComponentId markerId = ComponentManager.GetId<PlayerMarker>();
            ComponentId prefabId = ComponentManager.GetId<PrefabIdComponent>();
            ComponentId stateId = ComponentManager.GetId<PlayerState>();
            ComponentId nameId = ComponentManager.GetId<PlayerNameComponent>();
            ComponentId characterId = ComponentManager.GetId<PlayerCharacterComponent>();
            ArchetypeSignature signature = new ArchetypeSignature(
                new[] { markerId, stateId, prefabId, persistentTagId, nameId, characterId });

            for (int i = 0; i < count; i++)
                world.EnqueueCreateDefault(signature);

            world.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

            int playerIndex = 0;
            world.Query<PlayerState, PlayerCharacterComponent, PrefabIdComponent>(
                (Entity entity, ref PlayerState state, ref PlayerCharacterComponent character, ref PrefabIdComponent prefab) =>
                {
                    string playerName = $"Char{playerIndex + 1}";
                    prefab.PrefabId = (ushort)(((playerIndex & 1) == 0) ? PlayerArchetypeIds.Default : PlayerArchetypeIds.DefaultFemale);
                    character = new PlayerCharacterComponent
                    {
                        CharacterId = (ulong)(playerIndex + 1),
                        SlotIndex = (byte)(playerIndex % byte.MaxValue),
                    };
                    if (!world.TrySetComponent(entity, PlayerNameComponent.FromString(playerName)))
                        throw new InvalidOperationException("Failed to write PlayerNameComponent for parallel save player.");
                    state.X = playerIndex * 1.25f;
                    state.Y = (playerIndex % 7) * 0.5f;
                    state.Z = -playerIndex * 0.75f;
                    state.YawRadians = (playerIndex % 360) * 0.017453292f;
                    playerIndex++;
                });

            if (playerIndex < count)
                throw new InvalidOperationException($"Expected at least {count} player entities, found {playerIndex}.");
        }

        private static void CreateParallelItems(World world, int count)
        {
            ComponentId persistentTagId = ComponentManager.GetId<PersistentTag>();
            ComponentId prefabId = ComponentManager.GetId<PrefabIdComponent>();
            ComponentId itemTagId = ComponentManager.GetId<PerfItemTag>();
            ComponentId itemStateId = ComponentManager.GetId<PerfItemStateComponent>();
            ComponentId durabilityId = ComponentManager.GetId<PerfItemDurabilityComponent>();
            ArchetypeSignature signature = new ArchetypeSignature(new[] { persistentTagId, prefabId, itemTagId, itemStateId, durabilityId });

            for (int i = 0; i < count; i++)
                world.EnqueueCreateDefault(signature);

            world.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

            int itemIndex = 0;
            world.Query<PerfItemStateComponent, PerfItemDurabilityComponent, PrefabIdComponent>(
                (Entity entity, ref PerfItemStateComponent itemState, ref PerfItemDurabilityComponent durability, ref PrefabIdComponent prefab) =>
                {
                    prefab.PrefabId = (ushort)(5000 + (itemIndex % 64));
                    itemState = new PerfItemStateComponent
                    {
                        TemplateId = 100000 + itemIndex,
                        StackCount = 1 + (itemIndex % 20),
                        ValueCents = 25 + (itemIndex % 5000),
                    };
                    durability = new PerfItemDurabilityComponent
                    {
                        Durability = 0.25f + ((itemIndex % 100) / 100f),
                        Flags = (ushort)(itemIndex & 0xF),
                    };
                    itemIndex++;
                });

            if (itemIndex < count)
                throw new InvalidOperationException($"Expected at least {count} item entities, found {itemIndex}.");
        }

        private static void RunBaselineFullWorldSave(World world, PersistenceTypeRegistry registry, string root)
        {
            var paths = new SaveSetPaths(root);
            world.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
            paths.EnsureDirectoriesExist();

            int typeRecordCount = PersistenceTypeDatabaseSerializer.Write(paths.TypesDb, registry);
            int systemFileCount = SystemPersistenceSerializer.WriteAll(world, paths, registry);
            EntityPersistenceWriteResult entityResult = EntityPersistenceWriter.Write(world, paths, registry);

            var manifest = new SaveSetManifest(
                SavedAtUtcTicks: DateTime.UtcNow.Ticks,
                TypeRecordCount: typeRecordCount,
                SystemFileCount: systemFileCount,
                EntityBlockCount: entityResult.BlockCount,
                PersistentEntityCount: entityResult.PersistentEntityCount);

            SaveSetManifestSerializer.Write(paths.MetaBin, manifest);
        }

        private static void VerifyLoadedCounts(ServerRuntime runtime, int expectedPlayers, int expectedItems)
        {
            int loadedPlayers = 0;
            runtime.PlayerWorld.World.Query<PlayerCharacterComponent>((Entity entity, ref PlayerCharacterComponent character) => loadedPlayers++);

            int loadedItems = 0;
            runtime.PlayerWorld.World.Query<PerfItemStateComponent>((Entity entity, ref PerfItemStateComponent item) => loadedItems++);

            if (loadedPlayers != expectedPlayers)
                throw new InvalidOperationException($"Loaded player count mismatch. Expected {expectedPlayers}, got {loadedPlayers}.");
            if (loadedItems != expectedItems)
                throw new InvalidOperationException($"Loaded item count mismatch. Expected {expectedItems}, got {loadedItems}.");
        }

        private sealed class RuntimeHarnessContext
        {
            public RuntimeHarnessContext(
                ServerRuntime runtime,
                ServerAccountPersistenceCoordinator accountCoordinator,
                WorldPersistenceCoordinator worldCoordinator)
            {
                Runtime = runtime;
                AccountCoordinator = accountCoordinator;
                WorldCoordinator = worldCoordinator;
            }

            public ServerRuntime Runtime { get; }
            public ServerAccountPersistenceCoordinator AccountCoordinator { get; }
            public WorldPersistenceCoordinator WorldCoordinator { get; }
        }

        private static RuntimeHarnessContext CreateRuntime(string root)
        {
            string saveRoot = Path.Combine(root, "Saves");
            var accountCoordinator = new ServerAccountPersistenceCoordinator(
                options: new ServerAccountOptions(AccountsPerIp: 0),
                serverDataRoot: root);
            var worldCoordinator = new WorldPersistenceCoordinator(
                new WorldPersistenceOptions(
                    SaveDirectory: saveRoot,
                    RecoveryMode: WorldPersistenceRecoveryMode.Abort));
            var persistenceCoordinator = new ServerPersistenceCoordinator(accountCoordinator, worldCoordinator);

            ServerPersistenceRegistration.RegisterPersistentTypes(worldCoordinator);
            worldCoordinator.RegisterComponent<PerfItemStateComponent>();
            worldCoordinator.RegisterComponent<PerfItemDurabilityComponent>();

            var runtime = new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "server_persistence_perf",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false,
                    SaveDirectory: saveRoot,
                    AccountAuthenticationProvider: new TestAuthProvider()),
                hooks: new ServerRuntimeHooks
                {
                    Load = runtime => persistenceCoordinator.Load(runtime),
                    CaptureSaveSnapshot = runtime => persistenceCoordinator.CaptureSnapshot(runtime),
                    WriteSaveSnapshot = snapshot => persistenceCoordinator.WriteSnapshot((ServerPersistenceSaveSnapshot)snapshot),
                });
            return new RuntimeHarnessContext(runtime, accountCoordinator, worldCoordinator);
        }

        private static void EnsurePerfComponentsRegistered()
        {
            ServerPersistenceRegistration.EnsureComponentsRegistered();
            EnsureComponentRegistered<PerfItemTag>();
            EnsureComponentRegistered<PerfItemStateComponent>();
            EnsureComponentRegistered<PerfItemDurabilityComponent>();
        }

        private static void RegisterPerfSystems(World world, int count)
        {
            if (count < 0 || count > PerfPersistentSystemCatalog.Types.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    count,
                    $"Perf system count must be between 0 and {PerfPersistentSystemCatalog.Types.Length}.");
            }

            for (int i = 0; i < count; i++)
            {
                world.RegisterSystem(PerfPersistentSystemCatalog.Create(i));
            }
        }

        private static void EnsureComponentRegistered<T>() where T : unmanaged, IComponent<T>
        {
            if (ComponentManager.TryGetId(typeof(T), out _))
                return;

            if (ComponentManager.IsSealed)
                throw new InvalidOperationException($"Component {typeof(T).Name} is not registered and ComponentManager is already sealed.");

            ComponentManager.Register<T>();
        }

        private static void ResetStatics()
        {
            InvokeStaticClear("UltraSim.ECS.ComponentManager");
            InvokeStaticClear("UltraSim.ECS.PrefabRegistry");
        }

        private static void InvokeStaticClear(string typeName)
        {
            Type type = typeof(World).Assembly.GetType(typeName, throwOnError: true)!;
            MethodInfo clear = type.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic)!;
            clear.Invoke(null, null);
        }

        private static void PrintMeasurement(ScenarioMeasurement measurement)
        {
            Console.WriteLine(
                "PERSIST_PERF|" +
                $"scenario={measurement.Scenario.Kind}|" +
                $"players={measurement.Scenario.Players}|" +
                $"items={measurement.Scenario.Items}|" +
                $"iteration={measurement.Iteration}|" +
                $"save_ms={measurement.SaveMilliseconds:F3}|" +
                $"load_ms={measurement.LoadMilliseconds:F3}|" +
                $"account_save_ms={measurement.AccountSaveMilliseconds:F3}|" +
                $"world_save_ms={measurement.WorldSaveMilliseconds:F3}|" +
                $"world_save_structural_flush_ms={measurement.WorldSaveStructuralFlushMilliseconds:F3}|" +
                $"world_save_types_ms={measurement.WorldSaveTypeDatabaseMilliseconds:F3}|" +
                $"world_save_systems_ms={measurement.WorldSaveSystemsMilliseconds:F3}|" +
                $"world_save_entities_ms={measurement.WorldSaveEntitiesMilliseconds:F3}|" +
                $"world_save_manifest_ms={measurement.WorldSaveManifestMilliseconds:F3}|" +
                $"world_save_commit_ms={measurement.WorldSaveCommitMilliseconds:F3}|" +
                $"account_load_ms={measurement.AccountLoadMilliseconds:F3}|" +
                $"world_load_ms={measurement.WorldLoadMilliseconds:F3}|" +
                $"world_load_manifest_ms={measurement.WorldLoadManifestMilliseconds:F3}|" +
                $"world_load_types_ms={measurement.WorldLoadTypeValidationMilliseconds:F3}|" +
                $"world_load_systems_ms={measurement.WorldLoadSystemsMilliseconds:F3}|" +
                $"world_load_create_ms={measurement.WorldLoadEntityCreateMilliseconds:F3}|" +
                $"world_load_hydrate_ms={measurement.WorldLoadHydrateMilliseconds:F3}|" +
                $"world_load_fixup_ms={measurement.WorldLoadFixupMilliseconds:F3}|" +
                $"save_alloc_bytes={measurement.SaveGc.AllocatedBytes}|" +
                $"save_gc0={measurement.SaveGc.Gen0Collections}|" +
                $"save_gc1={measurement.SaveGc.Gen1Collections}|" +
                $"save_gc2={measurement.SaveGc.Gen2Collections}|" +
                $"load_alloc_bytes={measurement.LoadGc.AllocatedBytes}|" +
                $"load_gc0={measurement.LoadGc.Gen0Collections}|" +
                $"load_gc1={measurement.LoadGc.Gen1Collections}|" +
                $"load_gc2={measurement.LoadGc.Gen2Collections}|" +
                $"meta_bytes={measurement.MetaBytes}|" +
                $"types_bytes={measurement.TypesBytes}|" +
                $"entities_idx_bytes={measurement.EntitiesIdxBytes}|" +
                $"entities_bin_bytes={measurement.EntitiesBinBytes}|" +
                $"accounts_bytes={measurement.AccountsBytes}");
        }

        private static void PrintParallelSaveMeasurement(ParallelSaveMeasurement measurement)
        {
            Console.WriteLine(
                "PERSIST_PAR_SAVE|" +
                $"players={measurement.Scenario.Players}|" +
                $"items={measurement.Scenario.Items}|" +
                $"systems={measurement.Scenario.Systems}|" +
                $"iteration={measurement.Iteration}|" +
                $"baseline_world_save_ms={measurement.BaselineWorldSaveMilliseconds:F3}|" +
                $"partition_ms={measurement.PartitionMilliseconds:F3}|" +
                $"serial_total_ms={measurement.SerialTotalMilliseconds:F3}|" +
                $"serial_items_ms={measurement.SerialItemsMilliseconds:F3}|" +
                $"serial_mobiles_ms={measurement.SerialMobilesMilliseconds:F3}|" +
                $"serial_systems_ms={measurement.SerialSystemsMilliseconds:F3}|" +
                $"systems_parallel_total_ms={measurement.SystemsParallelTotalMilliseconds:F3}|" +
                $"systems_parallel_entities_ms={measurement.SystemsParallelEntityBranchMilliseconds:F3}|" +
                $"systems_parallel_systems_ms={measurement.SystemsParallelSystemsMilliseconds:F3}|" +
                $"systems_parallel_overhead_ms={measurement.SystemsParallelCoordinationOverheadMilliseconds:F3}|" +
                $"parallel_total_ms={measurement.ParallelTotalMilliseconds:F3}|" +
                $"parallel_items_ms={measurement.ParallelItemsMilliseconds:F3}|" +
                $"parallel_mobiles_ms={measurement.ParallelMobilesMilliseconds:F3}|" +
                $"parallel_systems_ms={measurement.ParallelSystemsMilliseconds:F3}|" +
                $"parallel_overhead_ms={measurement.ParallelCoordinationOverheadMilliseconds:F3}|" +
                $"baseline_alloc_bytes={measurement.BaselineGc.AllocatedBytes}|" +
                $"serial_alloc_bytes={measurement.SerialGc.AllocatedBytes}|" +
                $"systems_parallel_alloc_bytes={measurement.SystemsParallelGc.AllocatedBytes}|" +
                $"parallel_alloc_bytes={measurement.ParallelGc.AllocatedBytes}|" +
                $"baseline_gc0={measurement.BaselineGc.Gen0Collections}|" +
                $"serial_gc0={measurement.SerialGc.Gen0Collections}|" +
                $"systems_parallel_gc0={measurement.SystemsParallelGc.Gen0Collections}|" +
                $"parallel_gc0={measurement.ParallelGc.Gen0Collections}|" +
                $"item_entities={measurement.ItemEntityCount}|" +
                $"mobile_entities={measurement.MobileEntityCount}|" +
                $"system_files={measurement.SystemFileCount}");
        }

        private static void PrintSummary(Scenario scenario, IReadOnlyList<ScenarioMeasurement> measurements)
        {
            if (measurements.Count == 0)
                return;

            Console.WriteLine(
                "PERSIST_PERF_SUMMARY|" +
                $"scenario={scenario.Kind}|" +
                $"players={scenario.Players}|" +
                $"items={scenario.Items}|" +
                $"iterations={measurements.Count}|" +
                BuildSummaryFields("save_ms", measurements, static m => m.SaveMilliseconds) + "|" +
                BuildSummaryFields("load_ms", measurements, static m => m.LoadMilliseconds) + "|" +
                BuildSummaryFields("world_save_entities_ms", measurements, static m => m.WorldSaveEntitiesMilliseconds) + "|" +
                BuildSummaryFields("world_load_create_ms", measurements, static m => m.WorldLoadEntityCreateMilliseconds) + "|" +
                BuildSummaryFields("world_load_hydrate_ms", measurements, static m => m.WorldLoadHydrateMilliseconds) + "|" +
                BuildLongSummaryFields("save_alloc_bytes", measurements, static m => m.SaveGc.AllocatedBytes) + "|" +
                BuildLongSummaryFields("load_alloc_bytes", measurements, static m => m.LoadGc.AllocatedBytes) + "|" +
                $"save_gc_events={CountGcEvents(measurements, save: true)}|" +
                $"load_gc_events={CountGcEvents(measurements, save: false)}");
            Console.WriteLine();
        }

        private static void PrintParallelSaveSummary(
            ParallelSaveScenario scenario,
            IReadOnlyList<ParallelSaveMeasurement> measurements)
        {
            if (measurements.Count == 0)
                return;

            Console.WriteLine(
                "PERSIST_PAR_SAVE_SUMMARY|" +
                $"players={scenario.Players}|" +
                $"items={scenario.Items}|" +
                $"systems={scenario.Systems}|" +
                $"iterations={measurements.Count}|" +
                BuildParallelSummaryFields("baseline_world_save_ms", measurements, static m => m.BaselineWorldSaveMilliseconds) + "|" +
                BuildParallelSummaryFields("partition_ms", measurements, static m => m.PartitionMilliseconds) + "|" +
                BuildParallelSummaryFields("serial_total_ms", measurements, static m => m.SerialTotalMilliseconds) + "|" +
                BuildParallelSummaryFields("systems_parallel_total_ms", measurements, static m => m.SystemsParallelTotalMilliseconds) + "|" +
                BuildParallelSummaryFields("systems_parallel_overhead_ms", measurements, static m => m.SystemsParallelCoordinationOverheadMilliseconds) + "|" +
                BuildParallelSummaryFields("parallel_total_ms", measurements, static m => m.ParallelTotalMilliseconds) + "|" +
                BuildParallelSummaryFields("parallel_overhead_ms", measurements, static m => m.ParallelCoordinationOverheadMilliseconds) + "|" +
                BuildParallelSummaryFields("serial_systems_ms", measurements, static m => m.SerialSystemsMilliseconds) + "|" +
                BuildParallelSummaryFields("systems_parallel_systems_ms", measurements, static m => m.SystemsParallelSystemsMilliseconds) + "|" +
                BuildParallelSummaryFields("parallel_systems_ms", measurements, static m => m.ParallelSystemsMilliseconds) + "|" +
                BuildParallelLongSummaryFields("serial_alloc_bytes", measurements, static m => m.SerialGc.AllocatedBytes) + "|" +
                BuildParallelLongSummaryFields("systems_parallel_alloc_bytes", measurements, static m => m.SystemsParallelGc.AllocatedBytes) + "|" +
                BuildParallelLongSummaryFields("parallel_alloc_bytes", measurements, static m => m.ParallelGc.AllocatedBytes));
            Console.WriteLine();
        }

        private static string BuildSummaryFields(
            string prefix,
            IReadOnlyList<ScenarioMeasurement> measurements,
            Func<ScenarioMeasurement, double> selector)
        {
            double[] values = new double[measurements.Count];
            for (int i = 0; i < measurements.Count; i++)
                values[i] = selector(measurements[i]);

            Array.Sort(values);
            double min = values[0];
            double avg = Average(values);
            double p50 = Percentile(values, 0.50);
            double p95 = Percentile(values, 0.95);
            double max = values[^1];

            return
                $"{prefix}_min={min:F3}|" +
                $"{prefix}_avg={avg:F3}|" +
                $"{prefix}_p50={p50:F3}|" +
                $"{prefix}_p95={p95:F3}|" +
                $"{prefix}_max={max:F3}";
        }

        private static string BuildLongSummaryFields(
            string prefix,
            IReadOnlyList<ScenarioMeasurement> measurements,
            Func<ScenarioMeasurement, long> selector)
        {
            long[] values = new long[measurements.Count];
            for (int i = 0; i < measurements.Count; i++)
                values[i] = selector(measurements[i]);

            Array.Sort(values);
            long min = values[0];
            long avg = Average(values);
            long p50 = Percentile(values, 0.50);
            long p95 = Percentile(values, 0.95);
            long max = values[^1];

            return
                $"{prefix}_min={min}|" +
                $"{prefix}_avg={avg}|" +
                $"{prefix}_p50={p50}|" +
                $"{prefix}_p95={p95}|" +
                $"{prefix}_max={max}";
        }

        private static string BuildParallelSummaryFields(
            string prefix,
            IReadOnlyList<ParallelSaveMeasurement> measurements,
            Func<ParallelSaveMeasurement, double> selector)
        {
            double[] values = new double[measurements.Count];
            for (int i = 0; i < measurements.Count; i++)
                values[i] = selector(measurements[i]);

            Array.Sort(values);
            return
                $"{prefix}_min={values[0]:F3}|" +
                $"{prefix}_avg={Average(values):F3}|" +
                $"{prefix}_p50={Percentile(values, 0.50):F3}|" +
                $"{prefix}_p95={Percentile(values, 0.95):F3}|" +
                $"{prefix}_max={values[^1]:F3}";
        }

        private static string BuildParallelLongSummaryFields(
            string prefix,
            IReadOnlyList<ParallelSaveMeasurement> measurements,
            Func<ParallelSaveMeasurement, long> selector)
        {
            long[] values = new long[measurements.Count];
            for (int i = 0; i < measurements.Count; i++)
                values[i] = selector(measurements[i]);

            Array.Sort(values);
            return
                $"{prefix}_min={values[0]}|" +
                $"{prefix}_avg={Average(values)}|" +
                $"{prefix}_p50={Percentile(values, 0.50)}|" +
                $"{prefix}_p95={Percentile(values, 0.95)}|" +
                $"{prefix}_max={values[^1]}";
        }

        private static double Average(double[] values)
        {
            double total = 0;
            for (int i = 0; i < values.Length; i++)
                total += values[i];
            return total / values.Length;
        }

        private static long Average(long[] values)
        {
            long total = 0;
            for (int i = 0; i < values.Length; i++)
                total += values[i];
            return total / values.Length;
        }

        private static double Percentile(double[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 1)
                return sortedValues[0];

            double index = percentile * (sortedValues.Length - 1);
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);
            if (lower == upper)
                return sortedValues[lower];

            double weight = index - lower;
            return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * weight);
        }

        private static long Percentile(long[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 1)
                return sortedValues[0];

            double index = percentile * (sortedValues.Length - 1);
            int nearest = (int)Math.Round(index, MidpointRounding.AwayFromZero);
            return sortedValues[Math.Clamp(nearest, 0, sortedValues.Length - 1)];
        }

        private static int CountGcEvents(IReadOnlyList<ScenarioMeasurement> measurements, bool save)
        {
            int count = 0;
            for (int i = 0; i < measurements.Count; i++)
            {
                GcDelta delta = save ? measurements[i].SaveGc : measurements[i].LoadGc;
                if (delta.Gen0Collections > 0 || delta.Gen1Collections > 0 || delta.Gen2Collections > 0)
                    count++;
            }

            return count;
        }

        // ── Tick-window experiment ────────────────────────────────────────────────

        private const float TickWindowDeltaSeconds = 0.05f; // 20 Hz / 50 ms ticks

        private readonly record struct TickWindowMeasurement(
            Scenario Scenario,
            int Iteration,
            double[] BeforeTickMs,
            double SaveMs,
            double WorldSaveMs,
            double WorldSaveEntitiesMs,
            double AccountSaveMs,
            long SaveAllocBytes,
            double[] AfterTickMs);

        private static IEnumerable<Scenario> EnumerateTickWindowScenarios(HarnessOptions options)
        {
            for (int i = 0; i < options.PlayerCounts.Length; i++)
                for (int j = 0; j < options.ItemCounts.Length; j++)
                    yield return new Scenario("tickwindow", options.PlayerCounts[i], options.ItemCounts[j]);
        }

        private static TickWindowMeasurement RunTickWindowScenario(Scenario scenario, int iteration, int tickWindowTicks)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                $"persist_tw_{scenario.Name}_{iteration}_{Guid.NewGuid():N}");

            try
            {
                ResetStatics();
                EnsurePerfComponentsRegistered();
                RuntimeHarnessContext context = CreateRuntime(root);
                ServerRuntime runtime = context.Runtime;
                runtime.Start();

                PopulateRuntime(runtime, scenario.Players, scenario.Items);

                var beforeMs = new double[tickWindowTicks];
                var afterMs = new double[tickWindowTicks];
                var tickSw = new Stopwatch();

                for (int i = 0; i < tickWindowTicks; i++)
                {
                    tickSw.Restart();
                    runtime.Tick(TickWindowDeltaSeconds);
                    tickSw.Stop();
                    beforeMs[i] = tickSw.Elapsed.TotalMilliseconds;
                }

                double saveMs = MeasureMilliseconds(() => { runtime.Save(); }, out GcDelta saveGc);
                WorldPersistenceSaveTiming worldTiming = context.WorldCoordinator.LastSaveTiming;
                double accountSaveMs = context.AccountCoordinator.LastSaveTiming.TotalDuration.TotalMilliseconds;

                for (int i = 0; i < tickWindowTicks; i++)
                {
                    tickSw.Restart();
                    runtime.Tick(TickWindowDeltaSeconds);
                    tickSw.Stop();
                    afterMs[i] = tickSw.Elapsed.TotalMilliseconds;
                }

                runtime.Stop();

                return new TickWindowMeasurement(
                    scenario, iteration,
                    beforeMs, saveMs,
                    worldTiming.TotalDuration.TotalMilliseconds,
                    worldTiming.EntitiesWriteDuration.TotalMilliseconds,
                    accountSaveMs, saveGc.AllocatedBytes,
                    afterMs);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        private static void PrintTickWindowMeasurement(TickWindowMeasurement m)
        {
            var sortedBefore = (double[])m.BeforeTickMs.Clone();
            var sortedAfter = (double[])m.AfterTickMs.Clone();
            Array.Sort(sortedBefore);
            Array.Sort(sortedAfter);

            double beforeAvg = Average(sortedBefore);
            double afterAvg = Average(sortedAfter);

            Console.WriteLine(
                "TICK_WINDOW|" +
                $"scenario={m.Scenario.Kind}|" +
                $"players={m.Scenario.Players}|" +
                $"items={m.Scenario.Items}|" +
                $"iteration={m.Iteration}|" +
                $"before_ticks={m.BeforeTickMs.Length}|" +
                $"before_avg_ms={beforeAvg:F3}|" +
                $"before_p95_ms={Percentile(sortedBefore, 0.95):F3}|" +
                $"before_max_ms={sortedBefore[^1]:F3}|" +
                $"save_ms={m.SaveMs:F3}|" +
                $"world_save_ms={m.WorldSaveMs:F3}|" +
                $"world_save_entities_ms={m.WorldSaveEntitiesMs:F3}|" +
                $"account_save_ms={m.AccountSaveMs:F3}|" +
                $"save_alloc_bytes={m.SaveAllocBytes}|" +
                $"after_ticks={m.AfterTickMs.Length}|" +
                $"after_avg_ms={afterAvg:F3}|" +
                $"after_p95_ms={Percentile(sortedAfter, 0.95):F3}|" +
                $"after_max_ms={sortedAfter[^1]:F3}|" +
                $"tick_delta_avg_ms={afterAvg - beforeAvg:F3}");
        }

        private static void PrintTickWindowSummary(Scenario scenario, IReadOnlyList<TickWindowMeasurement> measurements)
        {
            if (measurements.Count == 0)
                return;

            // Reduce each iteration to its aggregate stats for cross-iteration summary
            var saveMsVals = new double[measurements.Count];
            var beforeAvgVals = new double[measurements.Count];
            var beforeP95Vals = new double[measurements.Count];
            var afterAvgVals = new double[measurements.Count];
            var afterP95Vals = new double[measurements.Count];
            var deltaAvgVals = new double[measurements.Count];

            for (int i = 0; i < measurements.Count; i++)
            {
                TickWindowMeasurement m = measurements[i];
                var sb = (double[])m.BeforeTickMs.Clone();
                var sa = (double[])m.AfterTickMs.Clone();
                Array.Sort(sb);
                Array.Sort(sa);
                double bAvg = Average(sb);
                double aAvg = Average(sa);
                saveMsVals[i] = m.SaveMs;
                beforeAvgVals[i] = bAvg;
                beforeP95Vals[i] = Percentile(sb, 0.95);
                afterAvgVals[i] = aAvg;
                afterP95Vals[i] = Percentile(sa, 0.95);
                deltaAvgVals[i] = aAvg - bAvg;
            }

            Array.Sort(saveMsVals);
            Array.Sort(beforeAvgVals);
            Array.Sort(beforeP95Vals);
            Array.Sort(afterAvgVals);
            Array.Sort(afterP95Vals);
            Array.Sort(deltaAvgVals);

            Console.WriteLine(
                "TICK_WINDOW_SUMMARY|" +
                $"scenario={scenario.Kind}|" +
                $"players={scenario.Players}|" +
                $"items={scenario.Items}|" +
                $"iterations={measurements.Count}|" +
                $"tick_window_ticks={measurements[0].BeforeTickMs.Length}|" +
                $"save_ms_min={saveMsVals[0]:F3}|save_ms_avg={Average(saveMsVals):F3}|save_ms_p95={Percentile(saveMsVals, 0.95):F3}|save_ms_max={saveMsVals[^1]:F3}|" +
                $"before_avg_ms_min={beforeAvgVals[0]:F3}|before_avg_ms_avg={Average(beforeAvgVals):F3}|before_avg_ms_p95={Percentile(beforeAvgVals, 0.95):F3}|before_avg_ms_max={beforeAvgVals[^1]:F3}|" +
                $"before_p95_ms_min={beforeP95Vals[0]:F3}|before_p95_ms_avg={Average(beforeP95Vals):F3}|before_p95_ms_max={beforeP95Vals[^1]:F3}|" +
                $"after_avg_ms_min={afterAvgVals[0]:F3}|after_avg_ms_avg={Average(afterAvgVals):F3}|after_avg_ms_p95={Percentile(afterAvgVals, 0.95):F3}|after_avg_ms_max={afterAvgVals[^1]:F3}|" +
                $"after_p95_ms_min={afterP95Vals[0]:F3}|after_p95_ms_avg={Average(afterP95Vals):F3}|after_p95_ms_max={afterP95Vals[^1]:F3}|" +
                $"tick_delta_avg_ms_min={deltaAvgVals[0]:F3}|tick_delta_avg_ms_avg={Average(deltaAvgVals):F3}|tick_delta_avg_ms_max={deltaAvgVals[^1]:F3}");
            Console.WriteLine();
        }

        // ── CSV / JSON export ─────────────────────────────────────────────────────

        private static readonly string[] CsvPerfColumns =
        {
            "run_at", "kind", "scenario", "players", "items", "iterations",
            "save_ms_avg", "save_ms_p95", "save_ms_max",
            "load_ms_avg", "load_ms_p95", "load_ms_max",
            "world_save_ms_avg", "world_save_ms_p95", "world_save_ms_max",
            "world_save_entities_ms_avg", "world_save_entities_ms_p95", "world_save_entities_ms_max",
            "world_load_hydrate_ms_avg", "world_load_hydrate_ms_p95", "world_load_hydrate_ms_max",
            "world_load_create_ms_avg", "world_load_create_ms_p95", "world_load_create_ms_max",
            "save_alloc_bytes_avg", "load_alloc_bytes_avg",
        };

        private static readonly string[] CsvTickWindowColumns =
        {
            "run_at", "kind", "scenario", "players", "items", "iterations", "tick_window_ticks",
            "save_ms_avg", "save_ms_p95", "save_ms_max",
            "world_save_ms_avg", "world_save_entities_ms_avg", "account_save_ms_avg",
            "before_avg_ms_avg", "before_p95_ms_avg", "before_avg_ms_max",
            "after_avg_ms_avg", "after_p95_ms_avg", "after_avg_ms_max",
            "tick_delta_avg_ms_avg",
        };

        private static void WriteCsv(
            string path,
            string runAt,
            IReadOnlyList<(Scenario Scenario, IReadOnlyList<ScenarioMeasurement> Measurements)> perfResults,
            IReadOnlyList<(Scenario Scenario, IReadOnlyList<TickWindowMeasurement> Measurements)> tickWindowResults)
        {
            bool fileExists = File.Exists(path);
            using var writer = new StreamWriter(path, append: true, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // Perf section
            if (perfResults.Count > 0)
            {
                if (!fileExists)
                    writer.WriteLine(string.Join(",", CsvPerfColumns));

                foreach ((Scenario scenario, IReadOnlyList<ScenarioMeasurement> measurements) in perfResults)
                {
                    double[] saveVals = SelectDoubles(measurements, static m => m.SaveMilliseconds);
                    double[] loadVals = SelectDoubles(measurements, static m => m.LoadMilliseconds);
                    double[] wSaveVals = SelectDoubles(measurements, static m => m.WorldSaveMilliseconds);
                    double[] wSaveEntVals = SelectDoubles(measurements, static m => m.WorldSaveEntitiesMilliseconds);
                    double[] wHydVals = SelectDoubles(measurements, static m => m.WorldLoadHydrateMilliseconds);
                    double[] wCreateVals = SelectDoubles(measurements, static m => m.WorldLoadEntityCreateMilliseconds);
                    long[] saveAllocVals = SelectLongs(measurements, static m => m.SaveGc.AllocatedBytes);
                    long[] loadAllocVals = SelectLongs(measurements, static m => m.LoadGc.AllocatedBytes);

                    writer.WriteLine(string.Join(",", new object[]
                    {
                        runAt, "perf", scenario.Kind, scenario.Players, scenario.Items, measurements.Count,
                        F(Average(saveVals)), F(Percentile(saveVals, 0.95)), F(saveVals[^1]),
                        F(Average(loadVals)), F(Percentile(loadVals, 0.95)), F(loadVals[^1]),
                        F(Average(wSaveVals)), F(Percentile(wSaveVals, 0.95)), F(wSaveVals[^1]),
                        F(Average(wSaveEntVals)), F(Percentile(wSaveEntVals, 0.95)), F(wSaveEntVals[^1]),
                        F(Average(wHydVals)), F(Percentile(wHydVals, 0.95)), F(wHydVals[^1]),
                        F(Average(wCreateVals)), F(Percentile(wCreateVals, 0.95)), F(wCreateVals[^1]),
                        Average(saveAllocVals), Average(loadAllocVals),
                    }));
                }
            }

            // Tick-window section
            if (tickWindowResults.Count > 0)
            {
                if (!fileExists && perfResults.Count == 0)
                    writer.WriteLine(string.Join(",", CsvTickWindowColumns));
                else if (tickWindowResults.Count > 0)
                    writer.WriteLine(); // blank separator between sections

                writer.WriteLine(string.Join(",", CsvTickWindowColumns));

                foreach ((Scenario scenario, IReadOnlyList<TickWindowMeasurement> measurements) in tickWindowResults)
                {
                    var saveMsVals = new double[measurements.Count];
                    var worldSaveMsVals = new double[measurements.Count];
                    var worldSaveEntVals = new double[measurements.Count];
                    var accountSaveVals = new double[measurements.Count];
                    var beforeAvgVals = new double[measurements.Count];
                    var beforeP95Vals = new double[measurements.Count];
                    var afterAvgVals = new double[measurements.Count];
                    var afterP95Vals = new double[measurements.Count];
                    var deltaVals = new double[measurements.Count];
                    int twTicks = measurements[0].BeforeTickMs.Length;

                    for (int i = 0; i < measurements.Count; i++)
                    {
                        TickWindowMeasurement m = measurements[i];
                        var sb = (double[])m.BeforeTickMs.Clone();
                        var sa = (double[])m.AfterTickMs.Clone();
                        Array.Sort(sb);
                        Array.Sort(sa);
                        double bAvg = Average(sb);
                        double aAvg = Average(sa);
                        saveMsVals[i] = m.SaveMs;
                        worldSaveMsVals[i] = m.WorldSaveMs;
                        worldSaveEntVals[i] = m.WorldSaveEntitiesMs;
                        accountSaveVals[i] = m.AccountSaveMs;
                        beforeAvgVals[i] = bAvg;
                        beforeP95Vals[i] = Percentile(sb, 0.95);
                        afterAvgVals[i] = aAvg;
                        afterP95Vals[i] = Percentile(sa, 0.95);
                        deltaVals[i] = aAvg - bAvg;
                    }

                    Array.Sort(saveMsVals);
                    Array.Sort(beforeAvgVals);
                    Array.Sort(beforeP95Vals);
                    Array.Sort(afterAvgVals);
                    Array.Sort(afterP95Vals);
                    Array.Sort(deltaVals);

                    writer.WriteLine(string.Join(",", new object[]
                    {
                        runAt, "tickwindow", scenario.Kind, scenario.Players, scenario.Items,
                        measurements.Count, twTicks,
                        F(Average(saveMsVals)), F(Percentile(saveMsVals, 0.95)), F(saveMsVals[^1]),
                        F(Average(worldSaveMsVals)), F(Average(worldSaveEntVals)), F(Average(accountSaveVals)),
                        F(Average(beforeAvgVals)), F(Average(beforeP95Vals)), F(beforeAvgVals[^1]),
                        F(Average(afterAvgVals)), F(Average(afterP95Vals)), F(afterAvgVals[^1]),
                        F(Average(deltaVals)),
                    }));
                }
            }

            Console.WriteLine($"CSV written: {path}");
        }

        private static void WriteJson(
            string path,
            string runAt,
            HarnessOptions options,
            IReadOnlyList<(Scenario Scenario, IReadOnlyList<ScenarioMeasurement> Measurements)> perfResults,
            IReadOnlyList<(Scenario Scenario, IReadOnlyList<TickWindowMeasurement> Measurements)> tickWindowResults)
        {
            var jsonOptions = new JsonWriterOptions { Indented = true };
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new Utf8JsonWriter(stream, jsonOptions);

            writer.WriteStartObject();
            writer.WriteString("runAt", runAt);
            writer.WriteNumber("iterations", options.Iterations);
            writer.WriteString("host", Environment.MachineName);
            writer.WriteNumber("cpuCount", Environment.ProcessorCount);

            writer.WriteStartArray("perfScenarios");
            foreach ((Scenario scenario, IReadOnlyList<ScenarioMeasurement> measurements) in perfResults)
            {
                writer.WriteStartObject();
                writer.WriteString("scenario", scenario.Kind);
                writer.WriteNumber("players", scenario.Players);
                writer.WriteNumber("items", scenario.Items);
                writer.WriteNumber("iterations", measurements.Count);

                double[] saveVals = SelectDoubles(measurements, static m => m.SaveMilliseconds);
                double[] loadVals = SelectDoubles(measurements, static m => m.LoadMilliseconds);
                double[] wSaveVals = SelectDoubles(measurements, static m => m.WorldSaveMilliseconds);
                double[] wSaveEntVals = SelectDoubles(measurements, static m => m.WorldSaveEntitiesMilliseconds);
                double[] wLoadHydVals = SelectDoubles(measurements, static m => m.WorldLoadHydrateMilliseconds);
                double[] wLoadCreateVals = SelectDoubles(measurements, static m => m.WorldLoadEntityCreateMilliseconds);
                long[] saveAllocVals = SelectLongs(measurements, static m => m.SaveGc.AllocatedBytes);
                long[] loadAllocVals = SelectLongs(measurements, static m => m.LoadGc.AllocatedBytes);

                WriteStatObject(writer, "save_ms", saveVals);
                WriteStatObject(writer, "load_ms", loadVals);
                WriteStatObject(writer, "world_save_ms", wSaveVals);
                WriteStatObject(writer, "world_save_entities_ms", wSaveEntVals);
                WriteStatObject(writer, "world_load_hydrate_ms", wLoadHydVals);
                WriteStatObject(writer, "world_load_create_ms", wLoadCreateVals);
                WriteLongStatObject(writer, "save_alloc_bytes", saveAllocVals);
                WriteLongStatObject(writer, "load_alloc_bytes", loadAllocVals);

                writer.WriteStartArray("measurements");
                foreach (ScenarioMeasurement m in measurements)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("iteration", m.Iteration);
                    writer.WriteNumber("save_ms", Round(m.SaveMilliseconds));
                    writer.WriteNumber("load_ms", Round(m.LoadMilliseconds));
                    writer.WriteNumber("world_save_ms", Round(m.WorldSaveMilliseconds));
                    writer.WriteNumber("world_save_entities_ms", Round(m.WorldSaveEntitiesMilliseconds));
                    writer.WriteNumber("world_load_hydrate_ms", Round(m.WorldLoadHydrateMilliseconds));
                    writer.WriteNumber("world_load_create_ms", Round(m.WorldLoadEntityCreateMilliseconds));
                    writer.WriteNumber("save_alloc_bytes", m.SaveGc.AllocatedBytes);
                    writer.WriteNumber("load_alloc_bytes", m.LoadGc.AllocatedBytes);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("tickWindowScenarios");
            foreach ((Scenario scenario, IReadOnlyList<TickWindowMeasurement> measurements) in tickWindowResults)
            {
                writer.WriteStartObject();
                writer.WriteString("scenario", scenario.Kind);
                writer.WriteNumber("players", scenario.Players);
                writer.WriteNumber("items", scenario.Items);
                writer.WriteNumber("iterations", measurements.Count);
                writer.WriteNumber("tickWindowTicks", measurements[0].BeforeTickMs.Length);

                var saveMsVals = new double[measurements.Count];
                var beforeAvgVals = new double[measurements.Count];
                var afterAvgVals = new double[measurements.Count];
                var deltaVals = new double[measurements.Count];

                for (int i = 0; i < measurements.Count; i++)
                {
                    TickWindowMeasurement m = measurements[i];
                    var sb = (double[])m.BeforeTickMs.Clone();
                    var sa = (double[])m.AfterTickMs.Clone();
                    Array.Sort(sb);
                    Array.Sort(sa);
                    saveMsVals[i] = m.SaveMs;
                    beforeAvgVals[i] = Average(sb);
                    afterAvgVals[i] = Average(sa);
                    deltaVals[i] = afterAvgVals[i] - beforeAvgVals[i];
                }

                WriteStatObject(writer, "save_ms", saveMsVals);
                WriteStatObject(writer, "before_tick_avg_ms", beforeAvgVals);
                WriteStatObject(writer, "after_tick_avg_ms", afterAvgVals);
                WriteStatObject(writer, "tick_delta_avg_ms", deltaVals);

                writer.WriteStartArray("measurements");
                foreach (TickWindowMeasurement m in measurements)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("iteration", m.Iteration);
                    writer.WriteNumber("save_ms", Round(m.SaveMs));
                    writer.WriteNumber("world_save_ms", Round(m.WorldSaveMs));
                    writer.WriteNumber("world_save_entities_ms", Round(m.WorldSaveEntitiesMs));
                    writer.WriteNumber("account_save_ms", Round(m.AccountSaveMs));
                    writer.WriteNumber("save_alloc_bytes", m.SaveAllocBytes);

                    var sb = (double[])m.BeforeTickMs.Clone();
                    var sa = (double[])m.AfterTickMs.Clone();
                    Array.Sort(sb);
                    Array.Sort(sa);
                    double bAvg = Average(sb);
                    double aAvg = Average(sa);

                    writer.WriteStartObject("before_tick");
                    writer.WriteNumber("avg_ms", Round(bAvg));
                    writer.WriteNumber("p95_ms", Round(Percentile(sb, 0.95)));
                    writer.WriteNumber("max_ms", Round(sb[^1]));
                    writer.WriteEndObject();

                    writer.WriteStartObject("after_tick");
                    writer.WriteNumber("avg_ms", Round(aAvg));
                    writer.WriteNumber("p95_ms", Round(Percentile(sa, 0.95)));
                    writer.WriteNumber("max_ms", Round(sa[^1]));
                    writer.WriteEndObject();

                    writer.WriteNumber("tick_delta_avg_ms", Round(aAvg - bAvg));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
            Console.WriteLine($"JSON written: {path}");
        }

        private static void WriteStatObject(Utf8JsonWriter writer, string name, double[] values)
        {
            Array.Sort(values);
            writer.WriteStartObject(name);
            writer.WriteNumber("min", Round(values[0]));
            writer.WriteNumber("avg", Round(Average(values)));
            writer.WriteNumber("p95", Round(Percentile(values, 0.95)));
            writer.WriteNumber("max", Round(values[^1]));
            writer.WriteEndObject();
        }

        private static void WriteLongStatObject(Utf8JsonWriter writer, string name, long[] values)
        {
            Array.Sort(values);
            writer.WriteStartObject(name);
            writer.WriteNumber("min", values[0]);
            writer.WriteNumber("avg", Average(values));
            writer.WriteNumber("p95", Percentile(values, 0.95));
            writer.WriteNumber("max", values[^1]);
            writer.WriteEndObject();
        }

        private static double[] SelectDoubles<T>(
            IReadOnlyList<T> source,
            Func<T, double> selector)
        {
            var result = new double[source.Count];
            for (int i = 0; i < source.Count; i++)
                result[i] = selector(source[i]);
            return result;
        }

        private static long[] SelectLongs<T>(
            IReadOnlyList<T> source,
            Func<T, long> selector)
        {
            var result = new long[source.Count];
            for (int i = 0; i < source.Count; i++)
                result[i] = selector(source[i]);
            return result;
        }

        private static double Round(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);

        private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
