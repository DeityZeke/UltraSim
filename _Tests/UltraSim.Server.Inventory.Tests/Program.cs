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
        Run(nameof(InMemoryInventoryStore_DispatchesDeltasOnlyToMatchingContainerViewers), InMemoryInventoryStore_DispatchesDeltasOnlyToMatchingContainerViewers);
        Run(nameof(InMemoryInventoryStore_CancelTransactionReturnsSuspendedItemsToSource), InMemoryInventoryStore_CancelTransactionReturnsSuspendedItemsToSource);
        Run(nameof(InMemoryInventoryStore_CancelTransactionEmitsUnresolvedReturnWhenSourceIsGone), InMemoryInventoryStore_CancelTransactionEmitsUnresolvedReturnWhenSourceIsGone);
        Run(nameof(InMemoryInventoryStore_EntityOwnerInvalidationClosesViewersAndReleasesTransactions), InMemoryInventoryStore_EntityOwnerInvalidationClosesViewersAndReleasesTransactions);
        Run(nameof(InventoryMoveMutationRules_UsesAccessRangeAndCapacityPolicies), InventoryMoveMutationRules_UsesAccessRangeAndCapacityPolicies);
        Run(nameof(InMemoryInventoryStore_ApplyMoveMutatesSnapshotsAndDispatchesScopedDeltas), InMemoryInventoryStore_ApplyMoveMutatesSnapshotsAndDispatchesScopedDeltas);
        Run(nameof(InMemoryInventoryStore_ApplyMoveRejectedByRulesDoesNotMutateSnapshots), InMemoryInventoryStore_ApplyMoveRejectedByRulesDoesNotMutateSnapshots);
        Run(nameof(InMemoryInventoryStore_ApplyMoveSupportsCrossPlacementModeDestination), InMemoryInventoryStore_ApplyMoveSupportsCrossPlacementModeDestination);
        Run(nameof(InMemoryInventoryStore_ApplyMoveRepositionsWithinSameContainer), InMemoryInventoryStore_ApplyMoveRepositionsWithinSameContainer);

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

    private static void InMemoryInventoryStore_DispatchesDeltasOnlyToMatchingContainerViewers()
    {
        var resolver = new FakeInventoryRuntimeRefResolver();
        var store = new InMemoryInventoryStore(resolver, new DefaultInventoryOwnerLifetimePolicy());

        InventoryContainerSnapshot chest = CreateContainer(
            "container:chest/x",
            new InventoryOwnerRef(InventoryOwnerKind.WorldObject, "world:chest/x"),
            InventoryContainerLifecycle.WorldPersistent);
        InventoryContainerSnapshot barrel = CreateContainer(
            "container:barrel/y",
            new InventoryOwnerRef(InventoryOwnerKind.WorldObject, "world:barrel/y"),
            InventoryContainerLifecycle.WorldPersistent);
        Assert.StoreSuccess(store.AddContainer(chest));
        Assert.StoreSuccess(store.AddContainer(barrel));

        Assert.StoreSuccess(store.OpenView(new InventoryContainerViewSnapshot(
            "view:player/chest",
            chest.ContainerId,
            new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:player"),
            InventoryAccessFlags.Read)));
        Assert.StoreSuccess(store.OpenView(new InventoryContainerViewSnapshot(
            "view:player/barrel",
            barrel.ContainerId,
            new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:player"),
            InventoryAccessFlags.Read)));
        Assert.StoreSuccess(store.OpenView(new InventoryContainerViewSnapshot(
            "view:gm/chest",
            chest.ContainerId,
            new InventoryRequesterRef(InventoryRequesterKind.Admin, "admin:gm"),
            InventoryAccessFlags.Read | InventoryAccessFlags.Admin)));

        Assert.Equal(2, store.ActiveContainerCount, "active container count");

        var delta = new InventoryContainerDelta(
            "delta:chest/x/1",
            chest.ContainerId,
            ContainerRevision: 1,
            InventoryContainerDeltaKind.QuantityChanged,
            ItemInstanceId: "item:coin/x",
            Item: null,
            PreviousPlacement: null,
            CurrentPlacement: null,
            QuantityDelta: -1,
            InventoryAccessFlags.Read);
        InventoryStoreOperationResult result = store.DispatchDelta(delta);
        Assert.StoreSuccess(result);
        Assert.Equal(1, result.Dispatches.Count, "dispatch count");
        Assert.Equal(2, result.Dispatches[0].RecipientViewIds.Count, "chest recipient count");
        Assert.Contains(result.Dispatches[0].RecipientViewIds, "view:player/chest", "chest player recipient");
        Assert.Contains(result.Dispatches[0].RecipientViewIds, "view:gm/chest", "chest gm recipient");
        Assert.NotContains(result.Dispatches[0].RecipientViewIds, "view:player/barrel", "barrel viewer isolation");
    }

    private static void InMemoryInventoryStore_CancelTransactionReturnsSuspendedItemsToSource()
    {
        var store = new InMemoryInventoryStore(new FakeInventoryRuntimeRefResolver(), new DefaultInventoryOwnerLifetimePolicy());
        InventoryContainerSnapshot left = CreateContainer(
            "container:trade/left",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:left"),
            InventoryContainerLifecycle.Transactional,
            [CreateCoinItem("item:coin/trade")]);
        InventoryContainerSnapshot right = CreateContainer(
            "container:trade/right",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:right"),
            InventoryContainerLifecycle.Transactional);
        Assert.StoreSuccess(store.AddContainer(left));
        Assert.StoreSuccess(store.AddContainer(right));

        Assert.StoreSuccess(store.BeginTransaction(new InventoryTransactionSnapshot(
            "trade:left_right/1",
            InventoryTransactionState.Open,
            [left.ContainerId, right.ContainerId],
            [])));

        Assert.StoreSuccess(store.SuspendWholeItem(
            "trade:left_right/1",
            left.ContainerId,
            right.ContainerId,
            "item:coin/trade"));
        Assert.True(store.TryGetContainer(left.ContainerId, out InventoryContainerSnapshot suspendedLeft), "left container after suspend");
        Assert.Equal(0, suspendedLeft.Items.Count, "left items after suspend");
        Assert.True(store.TryGetTransaction("trade:left_right/1", out InventoryTransactionSnapshot openTransaction), "open transaction exists");
        Assert.Equal(1, openTransaction.SuspendedItems.Count, "suspended item count");

        InventoryStoreOperationResult cancel = store.CancelTransaction("trade:left_right/1", "test cancel");
        Assert.StoreSuccess(cancel);
        Assert.ContainsEvent(cancel.Events, InventoryStoreEventKind.SuspendedItemReleased, "item:coin/trade");
        Assert.ContainsEvent(cancel.Events, InventoryStoreEventKind.TransactionCancelled, "trade:left_right/1");
        Assert.False(store.TryGetTransaction("trade:left_right/1", out _), "transaction removed after cancel");
        Assert.True(store.TryGetContainer(left.ContainerId, out InventoryContainerSnapshot restoredLeft), "left container after cancel");
        Assert.Equal(1, restoredLeft.Items.Count, "left items after cancel");
        Assert.Equal("item:coin/trade", restoredLeft.Items[0].ItemInstanceId, "restored item id");
    }

    private static void InMemoryInventoryStore_CancelTransactionEmitsUnresolvedReturnWhenSourceIsGone()
    {
        var store = new InMemoryInventoryStore(new FakeInventoryRuntimeRefResolver(), new DefaultInventoryOwnerLifetimePolicy());
        InventoryContainerSnapshot left = CreateContainer(
            "container:trade/gone_left",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:left"),
            InventoryContainerLifecycle.Transactional,
            [CreateCoinItem("item:coin/unresolved")]);
        InventoryContainerSnapshot right = CreateContainer(
            "container:trade/gone_right",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:right"),
            InventoryContainerLifecycle.Transactional);
        Assert.StoreSuccess(store.AddContainer(left));
        Assert.StoreSuccess(store.AddContainer(right));
        Assert.StoreSuccess(store.BeginTransaction(new InventoryTransactionSnapshot(
            "trade:gone_source/1",
            InventoryTransactionState.Open,
            [left.ContainerId, right.ContainerId],
            [])));
        Assert.StoreSuccess(store.SuspendWholeItem(
            "trade:gone_source/1",
            left.ContainerId,
            right.ContainerId,
            "item:coin/unresolved"));

        Assert.StoreSuccess(store.DestroyContainer(left.ContainerId, "source removed during transaction"));
        InventoryStoreOperationResult cancel = store.CancelTransaction("trade:gone_source/1", "test unresolved return");
        Assert.StoreSuccess(cancel);
        Assert.ContainsEvent(cancel.Events, InventoryStoreEventKind.SuspendedItemReturnUnresolved, "item:coin/unresolved");
        Assert.NotContainsEvent(cancel.Events, InventoryStoreEventKind.SuspendedItemReleased, "item:coin/unresolved");
        Assert.False(store.TryGetTransaction("trade:gone_source/1", out _), "transaction removed after unresolved cancel");
    }

    private static void InMemoryInventoryStore_EntityOwnerInvalidationClosesViewersAndReleasesTransactions()
    {
        World world = CreateWorld();
        Entity ownerEntity = world.CreateDefault(new ArchetypeSignature([]));
        string ownerRef = InventoryEcsEntityRefCodec.Format(ownerEntity);
        var store = new InMemoryInventoryStore(
            new EcsInventoryRuntimeRefResolver(world.Entities),
            new DefaultInventoryOwnerLifetimePolicy());

        InventoryContainerSnapshot left = CreateContainer(
            "container:entity_trade/left",
            new InventoryOwnerRef(InventoryOwnerKind.Entity, ownerRef),
            InventoryContainerLifecycle.Transactional,
            [CreateCoinItem("item:coin/entity_trade")]);
        InventoryContainerSnapshot right = CreateContainer(
            "container:entity_trade/right",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:right"),
            InventoryContainerLifecycle.Transactional);
        Assert.StoreSuccess(store.AddContainer(left));
        Assert.StoreSuccess(store.AddContainer(right));
        Assert.StoreSuccess(store.OpenView(new InventoryContainerViewSnapshot(
            "view:entity_trade/left",
            left.ContainerId,
            new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:watcher"),
            InventoryAccessFlags.Read)));
        Assert.StoreSuccess(store.BeginTransaction(new InventoryTransactionSnapshot(
            "trade:entity_right/1",
            InventoryTransactionState.Open,
            [left.ContainerId, right.ContainerId],
            [])));
        Assert.StoreSuccess(store.SuspendWholeItem(
            "trade:entity_right/1",
            left.ContainerId,
            right.ContainerId,
            "item:coin/entity_trade"));

        world.Destroy(ownerEntity);
        InventoryStoreOperationResult invalidated = store.RefreshOwnerLifetime(left.ContainerId);
        Assert.StoreSuccess(invalidated);

        Assert.ContainsEvent(invalidated.Events, InventoryStoreEventKind.OwnerInvalidated, left.ContainerId);
        Assert.ContainsEvent(invalidated.Events, InventoryStoreEventKind.ViewerClosed, "view:entity_trade/left");
        Assert.ContainsEvent(invalidated.Events, InventoryStoreEventKind.SuspendedItemReleased, "item:coin/entity_trade");
        Assert.ContainsEvent(invalidated.Events, InventoryStoreEventKind.TransactionCancelled, "trade:entity_right/1");
        Assert.False(store.TryGetTransaction("trade:entity_right/1", out _), "transaction removed after owner invalidation");
        Assert.True(store.TryGetContainer(left.ContainerId, out InventoryContainerSnapshot restoredLeft), "transactional container remains after owner invalidation");
        Assert.Equal(1, restoredLeft.Items.Count, "released item returned to source");
        Assert.Equal("item:coin/entity_trade", restoredLeft.Items[0].ItemInstanceId, "released item id");
        Assert.Equal(0, store.ActiveContainerCount, "active containers after owner invalidation");
    }

    private static void InventoryMoveMutationRules_UsesAccessRangeAndCapacityPolicies()
    {
        var requester = new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:mover");
        InventoryItemSnapshot sword = new InventoryItemSnapshot(
            "item:sword/move",
            "item.sword",
            Quantity: 1,
            MaxStackQuantity: 1,
            InventoryPlacementDescriptor.List(0));
        InventoryContainerSnapshot source = CreateContainer(
            "container:source/move",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:mover"),
            InventoryContainerLifecycle.Persistent,
            [sword]);
        InventoryContainerSnapshot destination = CreateContainer(
            "container:destination/move",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:mover"),
            InventoryContainerLifecycle.Persistent);
        var intent = new InventoryMutationIntent(
            "corr/move/rules",
            requester,
            ClientSequence: 41,
            InventoryMutationKind.MoveItem,
            source.ContainerId,
            destination.ContainerId,
            sword.ItemInstanceId,
            DestinationItemInstanceId: null,
            Quantity: 1,
            ProposedPlacement: InventoryPlacementDescriptor.List(0));
        Assert.Valid(intent.Validate());

        var access = new PublicTakePutAccessPolicy();
        var range = new ToggleRangePolicy();
        var capacity = new MaxItemCountCapacityPolicy(maxItems: 1);
        var rules = new InventoryMoveMutationRules(access, capacity, range);
        InventoryMutationRuleContext context = new InventoryMutationRuleContext(
            intent,
            InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForRequester(requester)),
            source,
            destination,
            sword,
            DestinationItem: null);

        InventoryMutationRuleDecision accepted = rules.Evaluate(context);
        Assert.True(accepted.IsAccepted, "move rule accepts baseline");
        InventoryMutationResult acceptedResult = accepted.ToResult(intent, [source.ContainerId, destination.ContainerId]);
        Assert.Valid(acceptedResult.Validate());

        range.Allow = false;
        InventoryMutationRuleDecision outOfRange = rules.Evaluate(context);
        Assert.False(outOfRange.IsAccepted, "move rule range rejection");
        Assert.Equal(InventoryMutationFailureReason.OutOfRange, outOfRange.FailureReason, "range failure reason");
        range.Allow = true;

        InventoryContainerSnapshot fullDestination = destination with { Items = [CreateCoinItem("item:coin/full_destination")] };
        InventoryMutationRuleDecision full = rules.Evaluate(context with { DestinationContainer = fullDestination });
        Assert.False(full.IsAccepted, "move rule capacity rejection");
        Assert.Equal(InventoryMutationFailureReason.RuleRejected, full.FailureReason, "capacity failure reason");

        var noPutAccess = new DenyPutAccessPolicy();
        var noPutRules = new InventoryMoveMutationRules(noPutAccess, capacity, range);
        InventoryMutationRuleDecision denied = noPutRules.Evaluate(context);
        Assert.False(denied.IsAccepted, "move rule access rejection");
        Assert.Equal(InventoryMutationFailureReason.Unauthorized, denied.FailureReason, "access failure reason");
    }

    private static void InMemoryInventoryStore_ApplyMoveMutatesSnapshotsAndDispatchesScopedDeltas()
    {
        var requester = new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:apply_move");
        var resolver = new FakeInventoryRuntimeRefResolver();
        resolver.RegisterRequester(requester);
        var store = new InMemoryInventoryStore(resolver, new DefaultInventoryOwnerLifetimePolicy());

        InventoryItemSnapshot coin = CreateCoinItem("item:coin/apply_move");
        InventoryContainerSnapshot source = CreateContainer(
            "container:move_apply/source",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:apply_move"),
            InventoryContainerLifecycle.Persistent,
            [coin]);
        InventoryContainerSnapshot destination = CreateContainer(
            "container:move_apply/destination",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:apply_move"),
            InventoryContainerLifecycle.Persistent);
        InventoryContainerSnapshot unrelated = CreateContainer(
            "container:move_apply/unrelated",
            new InventoryOwnerRef(InventoryOwnerKind.WorldObject, "world:unrelated"),
            InventoryContainerLifecycle.WorldPersistent);
        Assert.StoreSuccess(store.AddContainer(source));
        Assert.StoreSuccess(store.AddContainer(destination));
        Assert.StoreSuccess(store.AddContainer(unrelated));

        Assert.StoreSuccess(store.OpenView(new InventoryContainerViewSnapshot(
            "view:move_apply/source",
            source.ContainerId,
            requester,
            InventoryAccessFlags.Read)));
        Assert.StoreSuccess(store.OpenView(new InventoryContainerViewSnapshot(
            "view:move_apply/destination",
            destination.ContainerId,
            requester,
            InventoryAccessFlags.Read)));
        Assert.StoreSuccess(store.OpenView(new InventoryContainerViewSnapshot(
            "view:move_apply/unrelated",
            unrelated.ContainerId,
            requester,
            InventoryAccessFlags.Read)));

        InventoryMutationIntent intent = new InventoryMutationIntent(
            "corr:move_apply",
            requester,
            ClientSequence: 77,
            InventoryMutationKind.MoveItem,
            source.ContainerId,
            destination.ContainerId,
            coin.ItemInstanceId,
            DestinationItemInstanceId: null,
            Quantity: 1,
            ProposedPlacement: InventoryPlacementDescriptor.List(2));
        var rules = new InventoryMoveMutationRules(
            new PublicTakePutAccessPolicy(),
            new MaxItemCountCapacityPolicy(maxItems: 4),
            new ToggleRangePolicy());

        InventoryStoreOperationResult result = store.ApplyMove(intent, rules);
        Assert.StoreSuccess(result);
        Assert.True(result.MutationResult is not null, "move mutation result present");
        Assert.Equal(InventoryMutationResultKind.Accepted, result.MutationResult!.ResultKind, "move mutation result kind");
        Assert.Equal(intent.ClientSequence, result.MutationResult.ClientSequence, "move mutation sequence");
        Assert.Equal(2, result.Dispatches.Count, "move dispatch count");

        Assert.True(store.TryGetContainer(source.ContainerId, out InventoryContainerSnapshot updatedSource), "source container after move");
        Assert.True(store.TryGetContainer(destination.ContainerId, out InventoryContainerSnapshot updatedDestination), "destination container after move");
        Assert.Equal(0, updatedSource.Items.Count, "source items after move");
        Assert.Equal(1, updatedDestination.Items.Count, "destination items after move");
        Assert.Equal(2, updatedDestination.Items[0].Placement.Index, "destination item placement");

        Assert.Equal(InventoryContainerDeltaKind.ItemRemoved, result.Dispatches[0].Delta.Kind, "source delta kind");
        Assert.Contains(result.Dispatches[0].RecipientViewIds, "view:move_apply/source", "source delta recipient");
        Assert.NotContains(result.Dispatches[0].RecipientViewIds, "view:move_apply/destination", "source delta excludes destination");
        Assert.NotContains(result.Dispatches[0].RecipientViewIds, "view:move_apply/unrelated", "source delta excludes unrelated");

        Assert.Equal(InventoryContainerDeltaKind.ItemAdded, result.Dispatches[1].Delta.Kind, "destination delta kind");
        Assert.Contains(result.Dispatches[1].RecipientViewIds, "view:move_apply/destination", "destination delta recipient");
        Assert.NotContains(result.Dispatches[1].RecipientViewIds, "view:move_apply/source", "destination delta excludes source");
        Assert.NotContains(result.Dispatches[1].RecipientViewIds, "view:move_apply/unrelated", "destination delta excludes unrelated");
    }

    private static void InMemoryInventoryStore_ApplyMoveRejectedByRulesDoesNotMutateSnapshots()
    {
        var requester = new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:apply_reject");
        var resolver = new FakeInventoryRuntimeRefResolver();
        resolver.RegisterRequester(requester);
        var store = new InMemoryInventoryStore(resolver, new DefaultInventoryOwnerLifetimePolicy());

        InventoryItemSnapshot coin = CreateCoinItem("item:coin/apply_reject");
        InventoryContainerSnapshot source = CreateContainer(
            "container:move_reject/source",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:apply_reject"),
            InventoryContainerLifecycle.Persistent,
            [coin]);
        InventoryContainerSnapshot destination = CreateContainer(
            "container:move_reject/destination",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:apply_reject"),
            InventoryContainerLifecycle.Persistent);
        Assert.StoreSuccess(store.AddContainer(source));
        Assert.StoreSuccess(store.AddContainer(destination));

        InventoryMutationIntent intent = new InventoryMutationIntent(
            "corr:move_reject",
            requester,
            ClientSequence: 78,
            InventoryMutationKind.MoveItem,
            source.ContainerId,
            destination.ContainerId,
            coin.ItemInstanceId,
            DestinationItemInstanceId: null,
            Quantity: 1,
            ProposedPlacement: InventoryPlacementDescriptor.List(0));
        var range = new ToggleRangePolicy { Allow = false };
        var rules = new InventoryMoveMutationRules(
            new PublicTakePutAccessPolicy(),
            new MaxItemCountCapacityPolicy(maxItems: 4),
            range);

        InventoryStoreOperationResult result = store.ApplyMove(intent, rules);
        Assert.StoreSuccess(result);
        Assert.True(result.MutationResult is not null, "rejected move mutation result present");
        Assert.Equal(InventoryMutationResultKind.Rejected, result.MutationResult!.ResultKind, "rejected move result kind");
        Assert.Equal(InventoryMutationFailureReason.OutOfRange, result.MutationResult.FailureReason, "rejected move reason");
        Assert.Equal(0, result.Dispatches.Count, "rejected move dispatch count");

        Assert.True(store.TryGetContainer(source.ContainerId, out InventoryContainerSnapshot unchangedSource), "source container after rejected move");
        Assert.True(store.TryGetContainer(destination.ContainerId, out InventoryContainerSnapshot unchangedDestination), "destination container after rejected move");
        Assert.Equal(1, unchangedSource.Items.Count, "source items after rejected move");
        Assert.Equal(0, unchangedDestination.Items.Count, "destination items after rejected move");
        Assert.Equal(coin.ItemInstanceId, unchangedSource.Items[0].ItemInstanceId, "source item after rejected move");
    }

    private static void InMemoryInventoryStore_ApplyMoveSupportsCrossPlacementModeDestination()
    {
        var requester = new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:cross_mode");
        var resolver = new FakeInventoryRuntimeRefResolver();
        resolver.RegisterRequester(requester);
        var store = new InMemoryInventoryStore(resolver, new DefaultInventoryOwnerLifetimePolicy());

        InventoryItemSnapshot sourceItem = CreateCoinItem("item:coin/cross_mode");
        InventoryContainerSnapshot source = CreateContainer(
            "container:cross_mode/list_source",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:cross_mode"),
            InventoryContainerLifecycle.Persistent,
            [sourceItem]);
        InventoryContainerSnapshot destination = CreateContainer(
            "container:cross_mode/grid_destination",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:cross_mode"),
            InventoryContainerLifecycle.Persistent,
            InventoryPlacementMode.Grid);
        Assert.StoreSuccess(store.AddContainer(source));
        Assert.StoreSuccess(store.AddContainer(destination));

        InventoryMutationIntent intent = new InventoryMutationIntent(
            "corr:cross_mode",
            requester,
            ClientSequence: 79,
            InventoryMutationKind.MoveItem,
            source.ContainerId,
            destination.ContainerId,
            sourceItem.ItemInstanceId,
            DestinationItemInstanceId: null,
            Quantity: 1,
            ProposedPlacement: InventoryPlacementDescriptor.Grid(3, 4, 1, 1));
        var rules = new InventoryMoveMutationRules(
            new PublicTakePutAccessPolicy(),
            new MaxItemCountCapacityPolicy(maxItems: 4),
            new ToggleRangePolicy());

        InventoryStoreOperationResult result = store.ApplyMove(intent, rules);
        Assert.StoreSuccess(result);
        Assert.True(result.MutationResult is not null, "cross-mode move mutation result present");
        Assert.Equal(InventoryMutationResultKind.Accepted, result.MutationResult!.ResultKind, "cross-mode move result kind");
        Assert.True(store.TryGetContainer(destination.ContainerId, out InventoryContainerSnapshot updatedDestination), "grid destination after move");
        Assert.Equal(1, updatedDestination.Items.Count, "grid destination item count");
        Assert.Equal(InventoryPlacementMode.Grid, updatedDestination.Items[0].Placement.Mode, "grid destination placement mode");
        Assert.Equal(3, updatedDestination.Items[0].Placement.X, "grid destination placement x");
        Assert.Equal(4, updatedDestination.Items[0].Placement.Y, "grid destination placement y");
    }

    private static void InMemoryInventoryStore_ApplyMoveRepositionsWithinSameContainer()
    {
        var requester = new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:reposition");
        var resolver = new FakeInventoryRuntimeRefResolver();
        resolver.RegisterRequester(requester);
        var store = new InMemoryInventoryStore(resolver, new DefaultInventoryOwnerLifetimePolicy());

        InventoryItemSnapshot coin = CreateCoinItem("item:coin/reposition");
        InventoryContainerSnapshot backpack = CreateContainer(
            "container:reposition/backpack",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:reposition"),
            InventoryContainerLifecycle.Persistent,
            [coin]);
        Assert.StoreSuccess(store.AddContainer(backpack));
        Assert.StoreSuccess(store.OpenView(new InventoryContainerViewSnapshot(
            "view:reposition/backpack",
            backpack.ContainerId,
            requester,
            InventoryAccessFlags.Read)));

        InventoryMutationIntent intent = new InventoryMutationIntent(
            "corr:reposition",
            requester,
            ClientSequence: 80,
            InventoryMutationKind.MoveItem,
            backpack.ContainerId,
            backpack.ContainerId,
            coin.ItemInstanceId,
            DestinationItemInstanceId: null,
            Quantity: 1,
            ProposedPlacement: InventoryPlacementDescriptor.List(7));
        var rules = new InventoryMoveMutationRules(
            new PublicTakePutAccessPolicy(),
            new MaxItemCountCapacityPolicy(maxItems: 4),
            new ToggleRangePolicy());

        InventoryStoreOperationResult result = store.ApplyMove(intent, rules);
        Assert.StoreSuccess(result);
        Assert.True(result.MutationResult is not null, "reposition mutation result present");
        Assert.Equal(InventoryMutationResultKind.Accepted, result.MutationResult!.ResultKind, "reposition result kind");
        Assert.Equal(1, result.Dispatches.Count, "reposition dispatch count");
        Assert.Equal(InventoryContainerDeltaKind.ItemMoved, result.Dispatches[0].Delta.Kind, "reposition delta kind");
        Assert.Contains(result.Dispatches[0].RecipientViewIds, "view:reposition/backpack", "reposition recipient");
        Assert.True(store.TryGetContainer(backpack.ContainerId, out InventoryContainerSnapshot updated), "backpack after reposition");
        Assert.Equal(1, updated.Items.Count, "reposition item count");
        Assert.Equal(7, updated.Items[0].Placement.Index, "reposition index");
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
        return CreateContainer(containerId, owner, lifecycle, InventoryPlacementMode.List, items);
    }

    private static InventoryContainerSnapshot CreateContainer(
        string containerId,
        InventoryOwnerRef owner,
        InventoryContainerLifecycle lifecycle,
        InventoryPlacementMode placementMode,
        IReadOnlyList<InventoryItemSnapshot>? items = null)
    {
        return new InventoryContainerSnapshot(
            containerId,
            "container.test",
            owner,
            lifecycle,
            placementMode,
            [InventoryPermissionDescriptor.PublicReadTake],
            items ?? []);
    }

    private static InventoryItemSnapshot CreateCoinItem(string itemId)
    {
        return new InventoryItemSnapshot(
            itemId,
            "item.coin",
            Quantity: 1,
            MaxStackQuantity: 1,
            InventoryPlacementDescriptor.List(0));
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

internal sealed class PublicTakePutAccessPolicy : IInventoryContainerAccessPolicy
{
    public InventoryContainerAccessDecision Evaluate(in InventoryContainerAccessRequest request)
    {
        if (!request.RequesterResolution.IsResolved)
        {
            return InventoryContainerAccessDecision.Denied(
                InventoryMutationFailureReason.InvalidRequester,
                "Requester did not resolve.");
        }

        return InventoryContainerAccessDecision.Allowed(request.RequestedAccess);
    }
}

internal sealed class DenyPutAccessPolicy : IInventoryContainerAccessPolicy
{
    public InventoryContainerAccessDecision Evaluate(in InventoryContainerAccessRequest request)
    {
        if ((request.RequestedAccess & InventoryAccessFlags.Put) == InventoryAccessFlags.Put)
        {
            return InventoryContainerAccessDecision.Denied(
                InventoryMutationFailureReason.Unauthorized,
                "Put access denied.");
        }

        return InventoryContainerAccessDecision.Allowed(request.RequestedAccess);
    }
}

internal sealed class ToggleRangePolicy : IInventoryInteractionRangePolicy
{
    public bool Allow { get; set; } = true;

    public InventoryMutationRuleDecision IsInRange(
        InventoryRequesterRef requester,
        InventoryContainerSnapshot source,
        InventoryContainerSnapshot? destination)
    {
        return Allow
            ? InventoryMutationRuleDecision.Accepted()
            : InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.OutOfRange,
                "Requester is out of range.");
    }
}

internal sealed class MaxItemCountCapacityPolicy : IInventoryContainerCapacityPolicy
{
    private readonly int _maxItems;

    public MaxItemCountCapacityPolicy(int maxItems)
    {
        _maxItems = maxItems;
    }

    public InventoryMutationRuleDecision CanAccept(
        InventoryContainerSnapshot destination,
        InventoryItemSnapshot item,
        InventoryPlacementDescriptor? proposedPlacement)
    {
        return destination.Items.Count < _maxItems
            ? InventoryMutationRuleDecision.Accepted()
            : InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.RuleRejected,
                "Destination container is full.");
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

    public static void Equal(int expected, int actual, string label)
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

    public static void Equal(InventoryMutationResultKind expected, InventoryMutationResultKind actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected {label} to equal {expected}, got {actual}.");
        }
    }

    public static void Equal(InventoryContainerDeltaKind expected, InventoryContainerDeltaKind actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected {label} to equal {expected}, got {actual}.");
        }
    }

    public static void Equal(InventoryPlacementMode expected, InventoryPlacementMode actual, string label)
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

    public static void StoreSuccess(InventoryStoreOperationResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Expected store operation to succeed, got '{result.Error}'.");
        }
    }

    public static void Contains(IReadOnlyList<string> values, string expected, string label)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], expected, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Expected {label} to contain '{expected}'.");
    }

    public static void NotContains(IReadOnlyList<string> values, string unexpected, string label)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], unexpected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected {label} not to contain '{unexpected}'.");
            }
        }
    }

    public static void ContainsEvent(
        IReadOnlyList<InventoryStoreEvent> events,
        InventoryStoreEventKind kind,
        string id)
    {
        for (int i = 0; i < events.Count; i++)
        {
            InventoryStoreEvent candidate = events[i];
            if (candidate.Kind != kind)
            {
                continue;
            }

            if (string.Equals(candidate.ContainerId, id, StringComparison.Ordinal) ||
                string.Equals(candidate.ViewId, id, StringComparison.Ordinal) ||
                string.Equals(candidate.TransactionId, id, StringComparison.Ordinal) ||
                string.Equals(candidate.ItemInstanceId, id, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Expected event {kind} for '{id}'.");
    }

    public static void NotContainsEvent(
        IReadOnlyList<InventoryStoreEvent> events,
        InventoryStoreEventKind kind,
        string id)
    {
        for (int i = 0; i < events.Count; i++)
        {
            InventoryStoreEvent candidate = events[i];
            if (candidate.Kind != kind)
            {
                continue;
            }

            if (string.Equals(candidate.ContainerId, id, StringComparison.Ordinal) ||
                string.Equals(candidate.ViewId, id, StringComparison.Ordinal) ||
                string.Equals(candidate.TransactionId, id, StringComparison.Ordinal) ||
                string.Equals(candidate.ItemInstanceId, id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Did not expect event {kind} for '{id}'.");
            }
        }
    }
}
