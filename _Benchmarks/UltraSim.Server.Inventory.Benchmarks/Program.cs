using UltraSim.ECS.Benchmarks.Core;
using UltraSim.Inventory;
using UltraSim.Server.Inventory;

string outputPath = Path.Combine("_Benchmarks", "results", "server-inventory-benchmarks.json");
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--output")
    {
        outputPath = args[i + 1];
    }
}

BenchmarkRunReport report = MicroBenchmark.RunSuite(
    suiteName: "ServerInventoryBridge",
    kernelName: "UltraSim.Server.Inventory",
    scenarios: CreateScenarios());
MicroBenchmark.WriteReport(report, outputPath);
Console.WriteLine($"Wrote server inventory benchmark report to {outputPath}");

static IEnumerable<BenchmarkScenario> CreateScenarios()
{
    yield return new BenchmarkScenario
    {
        Name = "ApplyMove_SameContainer_Reposition_10000",
        Category = "InventoryStore",
        Comparable = false,
        Scale = 10_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunSameContainerMoveScenario(10_000),
    };

    yield return new BenchmarkScenario
    {
        Name = "ApplyMove_RejectedOutOfRange_10000",
        Category = "InventoryStore",
        Comparable = false,
        Scale = 10_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunRejectedMoveScenario(10_000),
    };
}

static ScenarioMeasurement RunSameContainerMoveScenario(int iterations)
{
    var requester = new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:bench_store_move");
    var store = new InMemoryInventoryStore(new StaticInventoryRuntimeRefResolver(requester), new DefaultInventoryOwnerLifetimePolicy());
    InventoryContainerSnapshot container = CreateContainer(
        "container:bench_store_move/backpack",
        InventoryPlacementMode.List,
        [CreateItem("item:bench_store_move/coin", InventoryPlacementDescriptor.List(0))]);
    StoreSuccess(store.AddContainer(container));
    StoreSuccess(store.OpenView(new InventoryContainerViewSnapshot(
        "view:bench_store_move/backpack",
        container.ContainerId,
        requester,
        InventoryAccessFlags.Read)));

    var rules = new InventoryMoveMutationRules(
        new AllowAllInventoryContainerAccessPolicy(),
        new MaxItemsInventoryCapacityPolicy(maxItems: 8),
        new ToggleInventoryRangePolicy(allow: true));

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        var intent = new InventoryMutationIntent(
            "corr:bench_store_move/reposition",
            requester,
            ClientSequence: i,
            InventoryMutationKind.MoveItem,
            container.ContainerId,
            container.ContainerId,
            "item:bench_store_move/coin",
            DestinationItemInstanceId: null,
            Quantity: 1,
            ProposedPlacement: InventoryPlacementDescriptor.List(i & 31));
        InventoryStoreOperationResult result = store.ApplyMove(intent, rules);
        if (!result.Succeeded || result.MutationResult is null || result.MutationResult.ResultKind != InventoryMutationResultKind.Accepted)
        {
            throw new InvalidOperationException(result.Error ?? result.MutationResult?.Message ?? "Move failed.");
        }

        checksum += result.Dispatches.Count + result.MutationResult.ClientSequence;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static ScenarioMeasurement RunRejectedMoveScenario(int iterations)
{
    var requester = new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:bench_store_reject");
    var store = new InMemoryInventoryStore(new StaticInventoryRuntimeRefResolver(requester), new DefaultInventoryOwnerLifetimePolicy());
    InventoryContainerSnapshot source = CreateContainer(
        "container:bench_store_reject/source",
        InventoryPlacementMode.List,
        [CreateItem("item:bench_store_reject/coin", InventoryPlacementDescriptor.List(0))]);
    InventoryContainerSnapshot destination = CreateContainer(
        "container:bench_store_reject/destination",
        InventoryPlacementMode.Grid);
    StoreSuccess(store.AddContainer(source));
    StoreSuccess(store.AddContainer(destination));

    var rules = new InventoryMoveMutationRules(
        new AllowAllInventoryContainerAccessPolicy(),
        new MaxItemsInventoryCapacityPolicy(maxItems: 8),
        new ToggleInventoryRangePolicy(allow: false));

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        var intent = new InventoryMutationIntent(
            "corr:bench_store_reject/out_of_range",
            requester,
            ClientSequence: i,
            InventoryMutationKind.MoveItem,
            source.ContainerId,
            destination.ContainerId,
            "item:bench_store_reject/coin",
            DestinationItemInstanceId: null,
            Quantity: 1,
            ProposedPlacement: InventoryPlacementDescriptor.Grid(0, 0, 1, 1));
        InventoryStoreOperationResult result = store.ApplyMove(intent, rules);
        if (!result.Succeeded || result.MutationResult is null || result.MutationResult.ResultKind != InventoryMutationResultKind.Rejected)
        {
            throw new InvalidOperationException(result.Error ?? result.MutationResult?.Message ?? "Rejected move benchmark failed.");
        }

        checksum += (long)result.MutationResult.FailureReason + result.MutationResult.ClientSequence;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static InventoryContainerSnapshot CreateContainer(
    string containerId,
    InventoryPlacementMode placementMode,
    IReadOnlyList<InventoryItemSnapshot>? items = null)
{
    return new InventoryContainerSnapshot(
        containerId,
        "container.benchmark",
        new InventoryOwnerRef(InventoryOwnerKind.Character, "character:bench_inventory"),
        InventoryContainerLifecycle.Persistent,
        placementMode,
        [InventoryPermissionDescriptor.PublicReadTake],
        items ?? []);
}

static InventoryItemSnapshot CreateItem(string itemId, InventoryPlacementDescriptor placement)
{
    return new InventoryItemSnapshot(
        itemId,
        "item.benchmark",
        Quantity: 1,
        MaxStackQuantity: 1,
        placement);
}

static void StoreSuccess(InventoryStoreOperationResult result)
{
    if (!result.Succeeded)
    {
        throw new InvalidOperationException(result.Error);
    }
}

internal sealed class StaticInventoryRuntimeRefResolver : IInventoryRuntimeRefResolver
{
    private readonly InventoryRequesterRef _requester;

    public StaticInventoryRuntimeRefResolver(InventoryRequesterRef requester)
    {
        _requester = requester;
    }

    public InventoryRuntimeRefResolution ResolveOwner(InventoryOwnerRef owner)
    {
        return InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForOwner(owner));
    }

    public InventoryRuntimeRefResolution ResolveRequester(InventoryRequesterRef requester)
    {
        return requester.Kind == _requester.Kind && string.Equals(requester.Id, _requester.Id, StringComparison.Ordinal)
            ? InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForRequester(requester))
            : InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.NotFound, "Requester was not registered.");
    }
}

internal sealed class AllowAllInventoryContainerAccessPolicy : IInventoryContainerAccessPolicy
{
    public InventoryContainerAccessDecision Evaluate(in InventoryContainerAccessRequest request)
    {
        return request.RequesterResolution.IsResolved
            ? InventoryContainerAccessDecision.Allowed(request.RequestedAccess)
            : InventoryContainerAccessDecision.Denied(InventoryMutationFailureReason.InvalidRequester, "Requester did not resolve.");
    }
}

internal sealed class MaxItemsInventoryCapacityPolicy : IInventoryContainerCapacityPolicy
{
    private readonly int _maxItems;

    public MaxItemsInventoryCapacityPolicy(int maxItems)
    {
        _maxItems = maxItems;
    }

    public InventoryMutationRuleDecision CanAccept(
        InventoryContainerSnapshot destination,
        InventoryItemSnapshot item,
        InventoryPlacementDescriptor? proposedPlacement)
    {
        return destination.Items.Count < _maxItems || ContainsItem(destination, item.ItemInstanceId)
            ? InventoryMutationRuleDecision.Accepted()
            : InventoryMutationRuleDecision.Rejected(InventoryMutationFailureReason.RuleRejected, "Destination container is full.");
    }

    private static bool ContainsItem(InventoryContainerSnapshot container, string itemInstanceId)
    {
        for (int i = 0; i < container.Items.Count; i++)
        {
            if (string.Equals(container.Items[i].ItemInstanceId, itemInstanceId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class ToggleInventoryRangePolicy : IInventoryInteractionRangePolicy
{
    private readonly bool _allow;

    public ToggleInventoryRangePolicy(bool allow)
    {
        _allow = allow;
    }

    public InventoryMutationRuleDecision IsInRange(
        InventoryRequesterRef requester,
        InventoryContainerSnapshot source,
        InventoryContainerSnapshot? destination)
    {
        return _allow
            ? InventoryMutationRuleDecision.Accepted()
            : InventoryMutationRuleDecision.Rejected(InventoryMutationFailureReason.OutOfRange, "Requester is out of range.");
    }
}
