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

    yield return new BenchmarkScenario
    {
        Name = "MutationIntent_Validate_Move_10000",
        Category = "InventoryMutation",
        Comparable = false,
        Scale = 10_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunMutationIntentValidationScenario(10_000),
    };

    yield return new BenchmarkScenario
    {
        Name = "MutationResult_Validate_Accepted_10000",
        Category = "InventoryMutation",
        Comparable = false,
        Scale = 10_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunMutationResultValidationScenario(10_000),
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

static ScenarioMeasurement RunMutationIntentValidationScenario(int iterations)
{
    var intent = new InventoryMutationIntent(
        "corr/bench/move",
        new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:bench"),
        ClientSequence: 1,
        InventoryMutationKind.MoveItem,
        SourceContainerId: "container:bench/list",
        DestinationContainerId: "container:bench/grid",
        SourceItemInstanceId: "item:list/1",
        DestinationItemInstanceId: null,
        Quantity: 1,
        ProposedPlacement: InventoryPlacementDescriptor.Grid(1, 1, 1, 1));

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        InventoryValidationResult result = intent.Validate();
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Error);
        }

        checksum += intent.ClientSequence + intent.Quantity + i;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static ScenarioMeasurement RunMutationResultValidationScenario(int iterations)
{
    var resultDocument = new InventoryMutationResult(
        "corr/bench/move",
        ClientSequence: 1,
        InventoryMutationKind.MoveItem,
        InventoryMutationResultKind.Accepted,
        InventoryMutationFailureReason.None,
        Message: null,
        AffectedContainerIds: ["container:bench/list", "container:bench/grid"]);

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        InventoryValidationResult result = resultDocument.Validate();
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Error);
        }

        checksum += resultDocument.ClientSequence + resultDocument.AffectedContainerIds.Count + i;
    }

    return new ScenarioMeasurement(iterations, checksum);
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
