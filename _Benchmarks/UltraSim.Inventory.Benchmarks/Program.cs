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

    yield return new BenchmarkScenario
    {
        Name = "ViewerRegistry_CountRecipients_1000Viewers_1000Deltas",
        Category = "InventorySession",
        Comparable = false,
        Scale = 1_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunViewerRegistryFanoutScenario(1_000),
    };

    yield return new BenchmarkScenario
    {
        Name = "TransactionSnapshot_Validate_1000SuspendedItems",
        Category = "InventoryTransaction",
        Comparable = false,
        Scale = 1_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunTransactionValidationScenario(CreateTransactionSnapshot(1_000)),
    };

    yield return new BenchmarkScenario
    {
        Name = "ListProjection_FromGridContainer_1000Items",
        Category = "InventoryProjection",
        Comparable = false,
        Scale = 1_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunListProjectionScenario(CreateGridContainer(1_000)),
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

static ScenarioMeasurement RunViewerRegistryFanoutScenario(int iterations)
{
    InventoryContainerViewerRegistrySnapshot registry = CreateViewerRegistry(1_000);
    InventoryValidationResult registryResult = registry.Validate();
    if (!registryResult.IsValid)
    {
        throw new InvalidOperationException(registryResult.Error);
    }

    var delta = new InventoryContainerDelta(
        "delta:bench/fanout",
        "container:bench/vendor",
        ContainerRevision: 2,
        InventoryContainerDeltaKind.QuantityChanged,
        ItemInstanceId: "item:bench/1",
        Item: null,
        PreviousPlacement: null,
        CurrentPlacement: null,
        QuantityDelta: -1,
        InventoryAccessFlags.Read,
        VisibilityScopeId: "scope/vendor/display");
    InventoryValidationResult deltaResult = delta.Validate(InventoryPlacementMode.List);
    if (!deltaResult.IsValid)
    {
        throw new InvalidOperationException(deltaResult.Error);
    }

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        checksum += registry.CountRecipients(delta);
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static ScenarioMeasurement RunTransactionValidationScenario(InventoryTransactionSnapshot transaction)
{
    InventoryValidationResult result = transaction.Validate();
    if (!result.IsValid)
    {
        throw new InvalidOperationException(result.Error);
    }

    return new ScenarioMeasurement(transaction.SuspendedItems.Count, transaction.SuspendedItems.Count + transaction.ParticipantContainerIds.Count);
}

static ScenarioMeasurement RunListProjectionScenario(InventoryContainerSnapshot container)
{
    InventoryListProjectionSnapshot projection = InventoryListProjectionSnapshot.FromContainer(
        "projection:bench/list_from_grid",
        container,
        containerRevision: 9,
        InventoryProjectionSortDescriptor.ByDefinitionId,
        viewId: "view:bench/list");
    InventoryValidationResult result = projection.Validate();
    if (!result.IsValid)
    {
        throw new InvalidOperationException(result.Error);
    }

    return new ScenarioMeasurement(projection.Rows.Count, projection.Rows.Count + (int)projection.SourcePlacementMode);
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

static InventoryContainerViewerRegistrySnapshot CreateViewerRegistry(int viewerCount)
{
    var viewers = new InventoryContainerViewSnapshot[viewerCount];
    for (int i = 0; i < viewerCount; i++)
    {
        InventoryAccessFlags access = InventoryAccessFlags.Read;
        string? scope = i % 2 == 0 ? "scope/vendor/display" : "scope/public";
        if (i % 64 == 0)
        {
            access |= InventoryAccessFlags.Admin;
            scope = null;
        }

        viewers[i] = new InventoryContainerViewSnapshot(
            $"view:bench/{i}",
            "container:bench/vendor",
            new InventoryRequesterRef(
                i % 64 == 0 ? InventoryRequesterKind.Admin : InventoryRequesterKind.PlayerSession,
                i % 64 == 0 ? $"admin:bench/{i}" : $"session:bench/{i}"),
            access,
            scope);
    }

    return new InventoryContainerViewerRegistrySnapshot("container:bench/vendor", Revision: 1, viewers);
}

static InventoryTransactionSnapshot CreateTransactionSnapshot(int suspendedItemCount)
{
    var suspended = new InventorySuspendedItemSnapshot[suspendedItemCount];
    for (int i = 0; i < suspendedItemCount; i++)
    {
        suspended[i] = new InventorySuspendedItemSnapshot(
            $"item:trade/{i}",
            i % 2 == 0 ? "container:trade/a" : "container:trade/b",
            i % 2 == 0 ? "container:trade/b" : "container:trade/a",
            Quantity: 1);
    }

    return new InventoryTransactionSnapshot(
        "trade:bench/1",
        InventoryTransactionState.Open,
        ["container:trade/a", "container:trade/b"],
        suspended);
}
