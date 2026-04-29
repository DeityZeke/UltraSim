using UltraSim.ECS;
using UltraSim.Inventory;
using UltraSim.Server.Inventory;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(EcsInventoryRuntimeRefResolver_UsesPackedEntityAndRejectsStaleHandles), EcsInventoryRuntimeRefResolver_UsesPackedEntityAndRejectsStaleHandles);
        Run(nameof(InventoryRuntimeRefResolver_FakeAndEcsResolversAgreeOnIntentRequesterShape), InventoryRuntimeRefResolver_FakeAndEcsResolversAgreeOnIntentRequesterShape);
        Run(nameof(EcsInventoryRuntimeRefResolver_LeavesNonEcsRefsAsExternalRuntimeIds), EcsInventoryRuntimeRefResolver_LeavesNonEcsRefsAsExternalRuntimeIds);
        Run(nameof(InventoryRulesFoundation_FakePoliciesEvaluateMutationAccessAndStackMerge), InventoryRulesFoundation_FakePoliciesEvaluateMutationAccessAndStackMerge);
        Run(nameof(DefaultInventoryOwnerLifetimePolicy_MapsStaleEntityOwnersToCloseReleaseOrDestroy), DefaultInventoryOwnerLifetimePolicy_MapsStaleEntityOwnersToCloseReleaseOrDestroy);

        Console.WriteLine("All server inventory bridge tests passed.");
    }

    private static void EcsInventoryRuntimeRefResolver_UsesPackedEntityAndRejectsStaleHandles()
    {
        World world = CreateWorld();
        Entity entity = world.CreateDefault(new ArchetypeSignature([]));
        var resolver = new EcsInventoryRuntimeRefResolver(world.Entities);

        string stableEntityId = InventoryEcsEntityRefCodec.Format(entity);
        var owner = new InventoryOwnerRef(InventoryOwnerKind.Entity, stableEntityId);
        var requester = new InventoryRequesterRef(InventoryRequesterKind.EntityAi, stableEntityId);

        InventoryRuntimeRefResolution ownerResult = resolver.ResolveOwner(owner);
        Assert.Resolved(ownerResult);
        Assert.True(ownerResult.Resolved.HasEntity, "owner entity flag");
        Assert.Equal(entity.Packed, ownerResult.Resolved.Entity.Packed, "owner packed entity");

        InventoryRuntimeRefResolution requesterResult = resolver.ResolveRequester(requester);
        Assert.Resolved(requesterResult);
        Assert.True(requesterResult.Resolved.HasEntity, "requester entity flag");
        Assert.Equal(entity.Packed, requesterResult.Resolved.Entity.Packed, "requester packed entity");

        world.Destroy(entity);

        InventoryRuntimeRefResolution staleOwnerResult = resolver.ResolveOwner(owner);
        Assert.NotResolved(staleOwnerResult, InventoryRuntimeRefResolutionKind.EntityNotAlive);

        Entity reused = world.CreateDefault(new ArchetypeSignature([]));
        Assert.Equal(entity.Index, reused.Index, "reused entity index");
        Assert.True(entity.Version != reused.Version, "reused entity version differs");

        InventoryRuntimeRefResolution oldPackedResult = resolver.ResolveOwner(owner);
        Assert.NotResolved(oldPackedResult, InventoryRuntimeRefResolutionKind.EntityNotAlive);

        InventoryRuntimeRefResolution newPackedResult = resolver.ResolveOwner(
            owner with { Id = InventoryEcsEntityRefCodec.Format(reused) });
        Assert.Resolved(newPackedResult);
        Assert.Equal(reused.Packed, newPackedResult.Resolved.Entity.Packed, "new packed entity");
    }

    private static void InventoryRuntimeRefResolver_FakeAndEcsResolversAgreeOnIntentRequesterShape()
    {
        World world = CreateWorld();
        Entity npc = world.CreateDefault(new ArchetypeSignature([]));
        string npcRef = InventoryEcsEntityRefCodec.Format(npc);

        var intent = new InventoryMutationIntent(
            "corr/npc/loot/bridge",
            new InventoryRequesterRef(InventoryRequesterKind.EntityAi, npcRef),
            ClientSequence: -1,
            InventoryMutationKind.MoveItem,
            SourceContainerId: "container:corpse/bridge",
            DestinationContainerId: "container:npc_pack/bridge",
            SourceItemInstanceId: "item:coin/bridge",
            DestinationItemInstanceId: null,
            Quantity: 1,
            ProposedPlacement: InventoryPlacementDescriptor.List(0));
        Assert.Valid(intent.Validate());

        var ecsResolver = new EcsInventoryRuntimeRefResolver(world.Entities);
        var fakeResolver = new FakeInventoryRuntimeRefResolver();
        fakeResolver.RegisterRequester(intent.Requester);

        InventoryRuntimeRefResolution ecsResult = ecsResolver.ResolveRequester(intent.Requester);
        InventoryRuntimeRefResolution fakeResult = fakeResolver.ResolveRequester(intent.Requester);

        Assert.Resolved(ecsResult);
        Assert.Resolved(fakeResult);
        Assert.Equal(ecsResult.Resolved.Entity.Packed, fakeResult.Resolved.Entity.Packed, "fake and ECS requester packed entity");
    }

    private static void EcsInventoryRuntimeRefResolver_LeavesNonEcsRefsAsExternalRuntimeIds()
    {
        World world = CreateWorld();
        var resolver = new EcsInventoryRuntimeRefResolver(world.Entities);

        var accountOwner = new InventoryOwnerRef(InventoryOwnerKind.Account, "account:alpha");
        InventoryRuntimeRefResolution ownerResult = resolver.ResolveOwner(accountOwner);
        Assert.Resolved(ownerResult);
        Assert.False(ownerResult.Resolved.HasEntity, "account owner entity flag");
        Assert.Equal("account:alpha", ownerResult.Resolved.StableId, "account owner stable id");

        var playerRequester = new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:alpha");
        InventoryRuntimeRefResolution requesterResult = resolver.ResolveRequester(playerRequester);
        Assert.Resolved(requesterResult);
        Assert.False(requesterResult.Resolved.HasEntity, "player requester entity flag");
        Assert.Equal("session:alpha", requesterResult.Resolved.StableId, "player requester stable id");

        var invalidEntityOwner = new InventoryOwnerRef(InventoryOwnerKind.Entity, "entity:not_a_number");
        Assert.NotResolved(resolver.ResolveOwner(invalidEntityOwner), InventoryRuntimeRefResolutionKind.InvalidEntityFormat);
    }

    private static void InventoryRulesFoundation_FakePoliciesEvaluateMutationAccessAndStackMerge()
    {
        var requester = new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:alpha");
        InventoryContainerSnapshot backpack = CreateContainer(
            "container:backpack/rules",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:alpha"),
            InventoryContainerLifecycle.Persistent,
            [
                new InventoryItemSnapshot(
                    "item:apple/source",
                    "item.apple",
                    Quantity: 5,
                    MaxStackQuantity: 20,
                    InventoryPlacementDescriptor.List(0)),
                new InventoryItemSnapshot(
                    "item:apple/destination",
                    "item.apple",
                    Quantity: 18,
                    MaxStackQuantity: 20,
                    InventoryPlacementDescriptor.List(1)),
            ]);

        var accessPolicy = new PublicReadTakeAccessPolicy();
        InventoryContainerAccessDecision access = accessPolicy.Evaluate(new InventoryContainerAccessRequest(
            requester,
            InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForRequester(requester)),
            backpack,
            InventoryAccessFlags.Read | InventoryAccessFlags.Take));
        Assert.True(access.IsAllowed, "public read/take access");

        InventoryContainerAccessDecision denied = accessPolicy.Evaluate(new InventoryContainerAccessRequest(
            requester,
            InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForRequester(requester)),
            backpack,
            InventoryAccessFlags.Mutate));
        Assert.False(denied.IsAllowed, "public mutate access");
        Assert.Equal(InventoryMutationFailureReason.Unauthorized, denied.FailureReason, "denied access failure reason");

        var mergePolicy = new SameDefinitionStackMergePolicy();
        InventoryStackMergeDecision merge = mergePolicy.Evaluate(new InventoryStackMergeRequest(
            requester,
            backpack.Items[0],
            backpack.Items[1],
            RequestedQuantity: 5,
            SourceContainer: backpack,
            DestinationContainer: backpack));
        Assert.True(merge.CanMerge, "stack merge allowed");
        Assert.Equal(2L, merge.QuantityToMove, "stack merge capped quantity");

        InventoryMutationIntent intent = new InventoryMutationIntent(
            "corr/rules/merge",
            requester,
            ClientSequence: 19,
            InventoryMutationKind.MergeStack,
            SourceContainerId: backpack.ContainerId,
            DestinationContainerId: backpack.ContainerId,
            SourceItemInstanceId: backpack.Items[0].ItemInstanceId,
            DestinationItemInstanceId: backpack.Items[1].ItemInstanceId,
            Quantity: 5,
            ProposedPlacement: null);
        Assert.Valid(intent.Validate());

        var mutationRules = new MergeMutationRules(mergePolicy);
        InventoryMutationRuleDecision decision = mutationRules.Evaluate(new InventoryMutationRuleContext(
            intent,
            InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForRequester(requester)),
            backpack,
            backpack,
            backpack.Items[0],
            backpack.Items[1]));
        Assert.True(decision.IsAccepted, "merge mutation decision");

        InventoryMutationResult result = decision.ToResult(intent, [backpack.ContainerId]);
        Assert.Valid(result.Validate());
        Assert.Equal(intent.ClientSequence, result.ClientSequence, "mutation result client sequence");
    }

    private static void DefaultInventoryOwnerLifetimePolicy_MapsStaleEntityOwnersToCloseReleaseOrDestroy()
    {
        var policy = new DefaultInventoryOwnerLifetimePolicy();
        var owner = new InventoryOwnerRef(InventoryOwnerKind.Entity, "entity:4294967297");
        InventoryRuntimeRefResolution staleOwner = InventoryRuntimeRefResolution.Failure(
            InventoryRuntimeRefResolutionKind.EntityNotAlive,
            "owner despawned");

        InventoryOwnerInvalidationDecision persistent = policy.Evaluate(
            CreateContainer("container:persistent/entity", owner, InventoryContainerLifecycle.Persistent),
            staleOwner);
        Assert.Equal(InventoryOwnerInvalidationAction.CloseViewers, persistent.Action, "persistent owner invalidation");
        Assert.True(persistent.ShouldCloseViewers, "persistent closes viewers");
        Assert.False(persistent.ShouldDestroyContainer, "persistent destroy flag");

        InventoryOwnerInvalidationDecision runtime = policy.Evaluate(
            CreateContainer("container:runtime/entity", owner, InventoryContainerLifecycle.Runtime),
            staleOwner);
        Assert.Equal(InventoryOwnerInvalidationAction.CloseViewersAndDestroyContainer, runtime.Action, "runtime owner invalidation");
        Assert.True(runtime.ShouldDestroyContainer, "runtime destroy flag");

        InventoryOwnerInvalidationDecision transactional = policy.Evaluate(
            CreateContainer("container:trade/entity", owner, InventoryContainerLifecycle.Transactional),
            staleOwner);
        Assert.Equal(InventoryOwnerInvalidationAction.CloseViewersAndReleaseTransactions, transactional.Action, "transaction owner invalidation");
        Assert.True(transactional.ShouldReleaseTransactions, "transaction release flag");

        InventoryOwnerInvalidationDecision accountFailure = policy.Evaluate(
            CreateContainer(
                "container:account/bank",
                new InventoryOwnerRef(InventoryOwnerKind.Account, "account:alpha"),
                InventoryContainerLifecycle.Persistent),
            InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.NotFound, "missing account"));
        Assert.Equal(InventoryOwnerInvalidationAction.CloseViewers, accountFailure.Action, "external owner invalidation");
    }

    private static World CreateWorld()
    {
        return new World(new EcsSchemaBuilder().Build());
    }

    private static InventoryContainerSnapshot CreateContainer(
        string containerId,
        InventoryOwnerRef owner,
        InventoryContainerLifecycle lifecycle,
        IReadOnlyList<InventoryItemSnapshot>? items = null)
    {
        return new InventoryContainerSnapshot(
            containerId,
            "container.test",
            owner,
            lifecycle,
            InventoryPlacementMode.List,
            [InventoryPermissionDescriptor.PublicReadTake],
            items ?? []);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
            Environment.ExitCode = 1;
            throw;
        }
    }
}

internal sealed class FakeInventoryRuntimeRefResolver : IInventoryRuntimeRefResolver
{
    private readonly HashSet<string> _ownerIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _requesterIds = new(StringComparer.Ordinal);
    private readonly HashSet<ulong> _aliveEntityIds = new();

    public void RegisterOwner(InventoryOwnerRef owner)
    {
        InventoryValidationResult validation = owner.Validate();
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Error, nameof(owner));
        }

        _ownerIds.Add(MakeOwnerKey(owner));
        if (owner.Kind == InventoryOwnerKind.Entity &&
            InventoryEcsEntityRefCodec.TryParse(owner.Id, out Entity entity))
        {
            _aliveEntityIds.Add(entity.Packed);
        }
    }

    public void RegisterRequester(InventoryRequesterRef requester)
    {
        InventoryValidationResult validation = requester.Validate();
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Error, nameof(requester));
        }

        _requesterIds.Add(MakeRequesterKey(requester));
        if (requester.Kind == InventoryRequesterKind.EntityAi &&
            InventoryEcsEntityRefCodec.TryParse(requester.Id, out Entity entity))
        {
            _aliveEntityIds.Add(entity.Packed);
        }
    }

    public InventoryRuntimeRefResolution ResolveOwner(InventoryOwnerRef owner)
    {
        InventoryValidationResult validation = owner.Validate();
        if (!validation.IsValid)
        {
            return InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.InvalidRef, validation.Error);
        }

        if (!_ownerIds.Contains(MakeOwnerKey(owner)))
        {
            return InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.NotFound, "Inventory owner ref was not registered.");
        }

        if (owner.Kind != InventoryOwnerKind.Entity)
        {
            return InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForOwner(owner));
        }

        if (!InventoryEcsEntityRefCodec.TryParse(owner.Id, out Entity entity))
        {
            return InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.InvalidEntityFormat, "Invalid entity owner ref.");
        }

        return _aliveEntityIds.Contains(entity.Packed)
            ? InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForOwnerEntity(owner, entity))
            : InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.EntityNotAlive, "Inventory entity owner is not alive.");
    }

    public InventoryRuntimeRefResolution ResolveRequester(InventoryRequesterRef requester)
    {
        InventoryValidationResult validation = requester.Validate();
        if (!validation.IsValid)
        {
            return InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.InvalidRef, validation.Error);
        }

        if (!_requesterIds.Contains(MakeRequesterKey(requester)))
        {
            return InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.NotFound, "Inventory requester ref was not registered.");
        }

        if (requester.Kind != InventoryRequesterKind.EntityAi)
        {
            return InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForRequester(requester));
        }

        if (!InventoryEcsEntityRefCodec.TryParse(requester.Id, out Entity entity))
        {
            return InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.InvalidEntityFormat, "Invalid entity requester ref.");
        }

        return _aliveEntityIds.Contains(entity.Packed)
            ? InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForRequesterEntity(requester, entity))
            : InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.EntityNotAlive, "Inventory entity requester is not alive.");
    }

    private static string MakeOwnerKey(InventoryOwnerRef owner)
    {
        return owner.Kind + "|" + owner.Id;
    }

    private static string MakeRequesterKey(InventoryRequesterRef requester)
    {
        return requester.Kind + "|" + requester.Id;
    }
}

internal sealed class PublicReadTakeAccessPolicy : IInventoryContainerAccessPolicy
{
    public InventoryContainerAccessDecision Evaluate(in InventoryContainerAccessRequest request)
    {
        if (!request.RequesterResolution.IsResolved)
        {
            return InventoryContainerAccessDecision.Denied(
                InventoryMutationFailureReason.InvalidRequester,
                "Requester did not resolve.");
        }

        for (int i = 0; i < request.Container.Permissions.Count; i++)
        {
            InventoryPermissionDescriptor permission = request.Container.Permissions[i];
            if (permission.Kind == InventoryPermissionKind.Public &&
                (permission.Access & request.RequestedAccess) == request.RequestedAccess)
            {
                return InventoryContainerAccessDecision.Allowed(permission.Access);
            }
        }

        return InventoryContainerAccessDecision.Denied(
            InventoryMutationFailureReason.Unauthorized,
            "Requested access was not granted.");
    }
}

internal sealed class SameDefinitionStackMergePolicy : IInventoryStackMergePolicy
{
    public InventoryStackMergeDecision Evaluate(in InventoryStackMergeRequest request)
    {
        if (!string.Equals(request.SourceItem.ItemDefinitionId, request.DestinationItem.ItemDefinitionId, StringComparison.Ordinal))
        {
            return InventoryStackMergeDecision.Denied(
                InventoryMutationFailureReason.RuleRejected,
                "Item definitions do not match.");
        }

        long capacity = request.DestinationItem.MaxStackQuantity - request.DestinationItem.Quantity;
        long quantity = Math.Min(request.RequestedQuantity, capacity);
        return quantity > 0
            ? InventoryStackMergeDecision.Allowed(quantity)
            : InventoryStackMergeDecision.Denied(
                InventoryMutationFailureReason.RuleRejected,
                "Destination stack is full.");
    }
}

internal sealed class MergeMutationRules : IInventoryMutationRules
{
    private readonly IInventoryStackMergePolicy _mergePolicy;

    public MergeMutationRules(IInventoryStackMergePolicy mergePolicy)
    {
        _mergePolicy = mergePolicy;
    }

    public InventoryMutationRuleDecision Evaluate(in InventoryMutationRuleContext context)
    {
        if (!context.Requester.IsResolved)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidRequester,
                "Requester did not resolve.");
        }

        if (context.Intent.Kind != InventoryMutationKind.MergeStack ||
            context.SourceItem is null ||
            context.DestinationItem is null)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.RuleRejected,
                "Only complete merge intents are supported by this fake policy.");
        }

        InventoryStackMergeDecision merge = _mergePolicy.Evaluate(new InventoryStackMergeRequest(
            context.Intent.Requester,
            context.SourceItem,
            context.DestinationItem,
            context.Intent.Quantity,
            context.SourceContainer,
            context.DestinationContainer));

        return merge.CanMerge
            ? InventoryMutationRuleDecision.Accepted()
            : InventoryMutationRuleDecision.Rejected(merge.FailureReason, merge.Message);
    }
}

internal static class Assert
{
    public static void Valid(InventoryValidationResult result)
    {
        if (!result.IsValid)
        {
            throw new InvalidOperationException($"Expected valid result, got '{result.Error}'.");
        }
    }

    public static void Resolved(InventoryRuntimeRefResolution result)
    {
        if (!result.IsResolved)
        {
            throw new InvalidOperationException($"Expected resolved result, got '{result.Kind}: {result.Error}'.");
        }
    }

    public static void NotResolved(InventoryRuntimeRefResolution result, InventoryRuntimeRefResolutionKind expectedKind)
    {
        if (result.IsResolved)
        {
            throw new InvalidOperationException("Expected unresolved result.");
        }

        if (result.Kind != expectedKind)
        {
            throw new InvalidOperationException($"Expected unresolved kind {expectedKind}, got {result.Kind}: {result.Error}.");
        }
    }

    public static void True(bool value, string label)
    {
        if (!value)
        {
            throw new InvalidOperationException($"Expected {label} to be true.");
        }
    }

    public static void False(bool value, string label)
    {
        if (value)
        {
            throw new InvalidOperationException($"Expected {label} to be false.");
        }
    }

    public static void Equal(ulong expected, ulong actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected {label} to equal {expected}, got {actual}.");
        }
    }

    public static void Equal(uint expected, uint actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected {label} to equal {expected}, got {actual}.");
        }
    }

    public static void Equal(long expected, long actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected {label} to equal {expected}, got {actual}.");
        }
    }

    public static void Equal(InventoryMutationFailureReason expected, InventoryMutationFailureReason actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected {label} to equal {expected}, got {actual}.");
        }
    }

    public static void Equal(InventoryOwnerInvalidationAction expected, InventoryOwnerInvalidationAction actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected {label} to equal {expected}, got {actual}.");
        }
    }

    public static void Equal(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {label} to equal '{expected}', got '{actual}'.");
        }
    }
}
