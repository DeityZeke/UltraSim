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

    private static World CreateWorld()
    {
        return new World(new EcsSchemaBuilder().Build());
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

    public static void Equal(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {label} to equal '{expected}', got '{actual}'.");
        }
    }
}
