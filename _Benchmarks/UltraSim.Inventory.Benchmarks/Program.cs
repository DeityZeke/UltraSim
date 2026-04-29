using UltraSim.ECS.Benchmarks.Core;
using UltraSim.Inventory;

string outputPath = Path.Combine("_Benchmarks", "results", "inventory-benchmarks.json");
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--output")
    {
        outputPath = args[i + 1];
    }
}

BenchmarkRunReport report = MicroBenchmark.RunSuite(
    suiteName: "InventoryKernel",
    kernelName: "UltraSim.Inventory",
    scenarios: CreateScenarios());
MicroBenchmark.WriteReport(report, outputPath);
Console.WriteLine($"Wrote inventory benchmark report to {outputPath}");

static IEnumerable<BenchmarkScenario> CreateScenarios()
{
    yield return new BenchmarkScenario
    {
        Name = "ContainerSnapshot_Validate_List_1000Items",
        Category = "InventoryValidation",
        Comparable = false,
        Scale = 1_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunContainerValidationScenario(CreateListContainer(1_000)),
    };

    yield return new BenchmarkScenario
    {
        Name = "ContainerSnapshot_Validate_Grid_1000Items",
        Category = "InventoryValidation",
        Comparable = false,
        Scale = 1_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunContainerValidationScenario(CreateGridContainer(1_000)),
    };
}

static ScenarioMeasurement RunContainerValidationScenario(InventoryContainerSnapshot container)
{
    InventoryValidationResult result = container.Validate();
    if (!result.IsValid)
    {
        throw new InvalidOperationException(result.Error);
    }

    return new ScenarioMeasurement(container.Items.Count, container.Items.Count + container.Permissions.Count);
}

static InventoryContainerSnapshot CreateListContainer(int itemCount)
{
    var items = new InventoryItemSnapshot[itemCount];
    for (int i = 0; i < itemCount; i++)
    {
        items[i] = new InventoryItemSnapshot(
            $"item:list/{i}",
            i % 4 == 0 ? "item.apple" : "item.iron_sword",
            Quantity: i % 4 == 0 ? 5 : 1,
            MaxStackQuantity: i % 4 == 0 ? 20 : 1,
            InventoryPlacementDescriptor.List(i));
    }

    return new InventoryContainerSnapshot(
        "container:bench/list",
        "container.backpack",
        new InventoryOwnerRef(InventoryOwnerKind.Character, "character:bench"),
        InventoryContainerLifecycle.Persistent,
        InventoryPlacementMode.List,
        [InventoryPermissionDescriptor.PublicReadTake],
        items);
}

static InventoryContainerSnapshot CreateGridContainer(int itemCount)
{
    var items = new InventoryItemSnapshot[itemCount];
    for (int i = 0; i < itemCount; i++)
    {
        int x = i % 40;
        int y = i / 40;
        items[i] = new InventoryItemSnapshot(
            $"item:grid/{i}",
            "item.grid_test",
            Quantity: 1,
            MaxStackQuantity: 1,
            InventoryPlacementDescriptor.Grid(x, y, width: 1, height: 1));
    }

    return new InventoryContainerSnapshot(
        "container:bench/grid",
        "container.grid_backpack",
        new InventoryOwnerRef(InventoryOwnerKind.Character, "character:bench"),
        InventoryContainerLifecycle.Persistent,
        InventoryPlacementMode.Grid,
        [InventoryPermissionDescriptor.PublicReadTake],
        items);
}
