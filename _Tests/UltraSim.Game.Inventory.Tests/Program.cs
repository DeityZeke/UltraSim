using System.Diagnostics;
using UltraSim.Client.Core;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Inventory;
using UltraSim.Game.Persistence;
using UltraSim.Game.UI;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("ClosedGameSchemaOwnsFinalInventoryIds", ClosedGameSchemaOwnsFinalInventoryIds),
    ("GameAggregateRejectsCrossWorldFeatureState", GameAggregateRejectsCrossWorldFeatureState),
    ("FixtureRebuildsDerivedIndexFromEcsTruth", FixtureRebuildsDerivedIndexFromEcsTruth),
    ("ContentAddsThirdItemWithoutInventoryRuleChange", ContentAddsThirdItemWithoutInventoryRuleChange),
    ("OwnerlessWorldContainerIsFirstClass", OwnerlessWorldContainerIsFirstClass),
    ("MoveSplitEquipAreAtomicAndPolicyDriven", MoveSplitEquipAreAtomicAndPolicyDriven),
    ("PackedChildPoolRepairsDonorLinks", PackedChildPoolRepairsDonorLinks),
    ("PackedChildPoolRandomizedDonorRepairMatchesEcsTruth", PackedChildPoolRandomizedDonorRepairMatchesEcsTruth),
    ("FailedRebuildPreservesPublishedIndex", FailedRebuildPreservesPublishedIndex),
    ("MostlyEmptyContainersDoNotReserveTheoreticalChildCapacity", MostlyEmptyContainersDoNotReserveTheoreticalChildCapacity),
    ("AccountingAdmissionAndInventoryShareGameGeneration", AccountingAdmissionAndInventoryShareGameGeneration),
    ("InventoryUiPublishesGranularRowDeltas", InventoryUiPublishesGranularRowDeltas),
    ("InventoryUiRejectsCrossWorldDurableIdentityAba", InventoryUiRejectsCrossWorldDurableIdentityAba),
    ("InventoryUiDeliveryFailureDetachesAfterAuthoritativeCommit", InventoryUiDeliveryFailureDetachesAfterAuthoritativeCommit),
    ("InventoryUiDoesNotConvertTerminalRuntimeFaultToDomainRejection", InventoryUiDoesNotConvertTerminalRuntimeFaultToDomainRejection),
    ("InventoryPersistsAndRebuildsAfterGenerationSwap", InventoryPersistsAndRebuildsAfterGenerationSwap),
    ("EquippedRelationshipPersistsAndReindexes", EquippedRelationshipPersistsAndReindexes),
    ("FailedInventoryRebuildDoesNotPublishCandidate", FailedInventoryRebuildDoesNotPublishCandidate),
    ("InventoryUiLifetimeIsOwnerBoundAndIdempotent", InventoryUiLifetimeIsOwnerBoundAndIdempotent),
    ("InventoryIndexBenchmarksDescribeMeasuredWork", InventoryIndexBenchmarksDescribeMeasuredWork),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} corrected S0.7 Game Inventory tests passed.");

static void ClosedGameSchemaOwnsFinalInventoryIds()
{
    _ = new UltraSimGameComposition();
    ComponentSchema schema = GameSchema.CreateSchema();
    Assert.Equal(8, schema.ComponentCount);
    ComponentId simulation = GameSchema.GetComponentId<GameSimulationState>();
    ComponentId character = GameSchema.GetComponentId<GameCharacter>();
    ComponentId container = GameSchema.GetComponentId<InventoryContainer>();
    ComponentId item = GameSchema.GetComponentId<InventoryItem>();
    Assert.True(simulation.Value < character.Value);
    Assert.True(character.Value < container.Value);
    Assert.True(container.Value != 0);
    Assert.True(item != container);
    PersistenceCatalog catalog = GameSchema.CreatePersistenceCatalog();
    Assert.Equal(schema.Id, catalog.Schema.Id);
    Assert.Equal(schema.ComponentCount, catalog.Schema.ComponentCount);
}

static void GameAggregateRejectsCrossWorldFeatureState()
{
    var composition = new UltraSimGameComposition(new InventoryLimits(4, 8, 4));
    GameStaging first = composition.CreateFresh(
        8, new PersistenceIdentityOptions(new PersistentWorldId(0x70_0101)));
    GameStaging second = composition.CreateFresh(
        8, new PersistenceIdentityOptions(new PersistentWorldId(0x70_0102)));
    using var accounting = new AccountingStore();
    try
    {
        Assert.Throws<ArgumentException>(() =>
            _ = new GameGenerationState(
                accounting,
                first.Inventory,
                second.Content,
                first.WorldState,
                first.SimulationLookup));
    }
    finally
    {
        first.WorldState.Dispose();
        first.Inventory.Dispose();
        first.World.Dispose();
        second.WorldState.Dispose();
        second.Inventory.Dispose();
        second.World.Dispose();
    }
}

static void FixtureRebuildsDerivedIndexFromEcsTruth()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, game) =>
    {
        InventoryGenerationState inventory = game.Inventory;
        InventoryItemView[] before = inventory.Snapshot(world, fixture.Backpack);
        Assert.Equal(2, before.Length);
        Assert.Equal(GamePrefabKeys.Bread, before[0].Prefab);
        Assert.Equal(5, before[0].Quantity);
        Assert.Equal(GamePrefabKeys.IronSword, before[1].Prefab);
        Entity[] indexedBefore = inventory.Index.GetItems(fixture.Backpack).ToArray();

        inventory.Rebuild(world);

        Assert.Equal(1, inventory.Index.ContainerCount);
        Assert.Equal(2, inventory.Index.ItemCount);
        Assert.SequenceEqual(indexedBefore, inventory.Index.GetItems(fixture.Backpack).ToArray());
        return true;
    });
}

static void ContentAddsThirdItemWithoutInventoryRuleChange()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, game) =>
    {
        Assert.True(game.Inventory.TryCreateItem(world, fixture.Backpack, game.Content.Apple, 4, out Entity apple));
        Assert.True(world.TryGetPrefabProvenance(apple, out PrefabKey key));
        Assert.Equal(GamePrefabKeys.Apple, key);
        Assert.True(game.Inventory.TryProjectItem(world, apple, out InventoryItemView view));
        Assert.Equal("Apple", view.DisplayName);
        Assert.Equal(4, view.Quantity);
        Assert.True(game.Inventory.Split(world, apple, fixture.Backpack, fixture.Backpack, 1).Accepted);
        Assert.Equal(4, game.Inventory.Index.ItemCount);
        return true;
    });
}

static void OwnerlessWorldContainerIsFirstClass()
{
    using Fixture fixture = CreateFixture();
    Entity chest = fixture.Use((world, _, game) =>
    {
        int aliveBefore = world.AliveCount;
        Assert.False(game.Inventory.TryCreateContainer(world, game.Content.Bread.Handle, 4, out Entity wrongKind));
        Assert.False(wrongKind.IsValid);
        Assert.False(game.Inventory.TryCreateOwnedContainer(
            world, game.Content.WorldChest, fixture.Character, 4, out Entity wrongOwnership));
        Assert.False(wrongOwnership.IsValid);
        Assert.Equal(aliveBefore, world.AliveCount);
        Assert.True(game.Inventory.TryCreateContainer(world, game.Content.WorldChest, 16, out Entity chest));
        Assert.False(game.Inventory.TryGetContainerOwner(world, chest, out Entity _owner));
        Assert.True(game.Inventory.TryCreateItem(world, chest, game.Content.Apple, 2, out Entity apple));
        Assert.Equal(1, game.Inventory.Index.GetItems(chest).Count);
        InventoryMutationResult equip = game.Inventory.Equip(
            world, apple, chest, Entity.Null, InventoryEquipSlot.MainHand);
        Assert.Equal(InventoryMutationStatus.MissingEquipOwner, equip.Status);
        return chest;
    });
    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    using var port = new RecordingInventoryUiPort(link);
    using var controller = new InventoryUiController(fixture.Runtime.Game, port);
    InventoryUiOpenResult opened = controller.Open(chest);
    Assert.True(opened.Succeeded);
    Assert.Equal("World Chest", client.GetValue(opened.View, InventoryUi.Title).Text);
}

static void MoveSplitEquipAreAtomicAndPolicyDriven()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, game) =>
    {
        InventoryGenerationState state = game.Inventory;
        Assert.True(state.TryCreateOwnedContainer(world, game.Content.Backpack, fixture.Character, 4, out Entity pouch));
        Assert.True(state.Move(world, fixture.Bread, fixture.Backpack, pouch).Accepted);
        Assert.Equal(InventoryMutationStatus.WrongSource,
            state.Move(world, fixture.Bread, fixture.Backpack, pouch).Status);

        InventoryMutationResult split = state.Split(world, fixture.Bread, pouch, fixture.Backpack, 2);
        Assert.True(split.Accepted);
        Assert.Equal(3, ReadItem(world, state, fixture.Bread).Quantity);
        Assert.Equal(2, ReadItem(world, state, split.CreatedItem).Quantity);
        Assert.Equal(InventoryMutationStatus.InvalidQuantity,
            state.Split(world, fixture.Bread, pouch, fixture.Backpack, 0).Status);

        Assert.Equal(InventoryMutationStatus.ItemPolicyViolation,
            state.Equip(
                world, split.CreatedItem, fixture.Backpack, fixture.Character, InventoryEquipSlot.MainHand).Status);
        Assert.True(state.Equip(
            world, fixture.Sword, fixture.Backpack, fixture.Character, InventoryEquipSlot.MainHand).Accepted);
        Assert.True(state.Index.TryGetEquipped(
            fixture.Character, InventoryEquipSlot.MainHand, out Entity equipped));
        Assert.Equal(fixture.Sword, equipped);
        return true;
    });
}

static void PackedChildPoolRepairsDonorLinks()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, game) =>
    {
        InventoryGenerationState state = game.Inventory;
        Assert.True(state.TryCreateOwnedContainer(world, game.Content.Backpack, fixture.Character, 8, out Entity pouch));
        Assert.True(state.TryCreateItem(world, pouch, game.Content.Bread, 2, out Entity first));
        Assert.True(state.TryCreateItem(world, pouch, game.Content.Bread, 2, out Entity middle));
        Assert.True(state.TryCreateItem(world, pouch, game.Content.Bread, 2, out Entity donor));
        Assert.True(state.Index.TryGetLocation(middle, out InventoryLocation removedBefore));
        Assert.True(state.Index.TryGetLocation(donor, out InventoryLocation donorBefore));
        Assert.Equal(pouch, removedBefore.Parent);
        Assert.Equal(pouch, donorBefore.Parent);

        Assert.True(state.DestroyItem(world, middle).Accepted);

        Assert.True(state.Index.TryGetLocation(donor, out InventoryLocation donorAfter));
        Assert.Equal(pouch, donorAfter.Parent);
        Assert.Equal(InventoryEquipSlot.None, donorAfter.EquipSlot);
        Assert.SequenceEqual(new[] { first, donor }, state.Index.GetItems(pouch).ToArray());
        Assert.True(state.Move(world, donor, pouch, fixture.Backpack).Accepted);
        Assert.SequenceEqual(new[] { first }, state.Index.GetItems(pouch).ToArray());
        return true;
    });
}

static void PackedChildPoolRandomizedDonorRepairMatchesEcsTruth()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, game) =>
    {
        InventoryGenerationState state = game.Inventory;
        var containers = new List<Entity> { fixture.Backpack };
        for (int index = 0; index < 3; index++)
        {
            Assert.True(state.TryCreateContainer(
                world, game.Content.WorldChest, capacity: 16, out Entity container));
            containers.Add(container);
        }
        var items = state.Index.GetItems(fixture.Backpack).ToArray().ToList();
        for (int index = 0; index < 32; index++)
        {
            Entity destination = containers[1 + (index % 3)];
            Assert.True(state.TryCreateItem(world, destination, game.Content.Bread, 1, out Entity item));
            items.Add(item);
        }

        var random = new Random(0x50707);
        for (int operation = 0; operation < 5_000; operation++)
        {
            if (items.Count != 0 && random.Next(5) == 0)
            {
                int itemIndex = random.Next(items.Count);
                Entity item = items[itemIndex];
                if (state.DestroyItem(world, item).Accepted)
                    items.RemoveAt(itemIndex);
            }
            else if (items.Count < 48 && random.Next(4) == 0)
            {
                Entity destination = containers[random.Next(containers.Count)];
                if (state.TryCreateItem(world, destination, game.Content.Bread, 1, out Entity created))
                    items.Add(created);
            }
            else if (items.Count != 0)
            {
                Entity item = items[random.Next(items.Count)];
                InventoryItem value = ReadItem(world, state, item);
                if (value.EquipSlot == InventoryEquipSlot.None)
                {
                    Entity destination = containers[random.Next(containers.Count)];
                    InventoryMutationResult ignored = state.Move(
                        world, item, value.Parent, destination);
                }
            }

            if ((operation & 31) == 0)
                AssertInventoryIndexMatchesTruth(world, state, containers, items);
        }
        AssertInventoryIndexMatchesTruth(world, state, containers, items);
        return true;
    });
}

static void AssertInventoryIndexMatchesTruth(
    EcsWorld world,
    InventoryGenerationState state,
    IReadOnlyList<Entity> containers,
    IReadOnlyList<Entity> items)
{
    var indexed = new HashSet<Entity>();
    foreach (Entity container in containers)
    {
        foreach (Entity item in state.Index.GetItems(container))
        {
            Assert.True(indexed.Add(item));
            InventoryItem value = ReadItem(world, state, item);
            Assert.Equal(container, value.Parent);
            Assert.Equal(InventoryEquipSlot.None, value.EquipSlot);
            Assert.True(state.Index.TryGetLocation(item, out InventoryLocation location));
            Assert.Equal(container, location.Parent);
        }
    }
    Assert.Equal(items.Count, state.Index.ItemCount);
    Assert.Equal(items.Count, indexed.Count);
    foreach (Entity item in items)
        Assert.True(indexed.Contains(item));
}

static void FailedRebuildPreservesPublishedIndex()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, game) =>
    {
        InventoryGenerationState state = game.Inventory;
        Entity[] before = state.Index.GetItems(fixture.Backpack).ToArray();
        InventoryIndexSnapshot snapshot = state.Index.CaptureSnapshot();
        Assert.True(world.TryDestroy(fixture.Backpack));

        Assert.Throws<InvalidOperationException>(() => state.Rebuild(world));
        Assert.Equal(snapshot, state.Index.CaptureSnapshot());
        Assert.SequenceEqual(before, state.Index.GetItems(fixture.Backpack).ToArray());
        return true;
    });
}

static void MostlyEmptyContainersDoNotReserveTheoreticalChildCapacity()
{
    const int containers = 100_000;
    const int gameplayCapacity = 20;
    var limits = new InventoryLimits(containers, 32, gameplayCapacity);
    var composition = new UltraSimGameComposition(limits, maximumChunksPerContentArchetype: 256);
    GameStaging staging = composition.CreateFresh(
        containers + 8,
        new PersistenceIdentityOptions(new PersistentWorldId(0x70_1000)),
        limits);
    try
    {
        staging.World.Seal();
        staging.World.Initialize();
        staging.World.BeginRun();
        CollectGarbage();
        long managedAtZero = GC.GetTotalMemory(forceFullCollection: false);
        long managedAtTenThousand = 0;
        long workingSetAtTenThousand = 0;
        using Process process = Process.GetCurrentProcess();
        for (int index = 0; index < containers; index++)
        {
            if (!staging.Inventory.TryCreateContainer(
                    staging.World, staging.Content.WorldChest, gameplayCapacity, out _))
            {
                throw new InvalidOperationException($"Could not create empty container {index}.");
            }
            if (index == 9_999)
            {
                CollectGarbage();
                managedAtTenThousand = GC.GetTotalMemory(forceFullCollection: false);
                workingSetAtTenThousand = process.WorkingSet64;
            }
        }
        CollectGarbage();
        long managedAtOneHundredThousand = GC.GetTotalMemory(forceFullCollection: false);
        long workingSetAtOneHundredThousand = process.WorkingSet64;
        InventoryIndexSnapshot snapshot = staging.Inventory.Index.CaptureSnapshot();
        Assert.Equal(containers, snapshot.ContainerCount);
        Assert.Equal(0, snapshot.ContainedItemCount);
        Assert.Equal(limits.MaximumItems, snapshot.ChildNodeCapacity);
        Assert.True(snapshot.ChildNodeCapacity < containers * gameplayCapacity);
        Console.WriteLine(
            $"SCALE S0.7 empty_containers={containers} theoretical_slots={containers * gameplayCapacity} " +
            $"child_pool_slots={snapshot.ChildNodeCapacity} known_child_bytes={snapshot.KnownChildStorageBytes} " +
            $"managed_0={managedAtZero} managed_10k={managedAtTenThousand} " +
            $"managed_100k={managedAtOneHundredThousand} " +
            $"working_set_10k={workingSetAtTenThousand} working_set_100k={workingSetAtOneHundredThousand} " +
            $"excluded=\"{InventoryIndexSnapshot.Exclusions}\"");
    }
    finally
    {
        staging.WorldState.Dispose();
        staging.Inventory.Dispose();
        staging.World.Dispose();
    }
}

static void AccountingAdmissionAndInventoryShareGameGeneration()
{
    using Fixture fixture = CreateFixture();
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    using var admissionUi = new LocalUiSemanticPort(link);
    using var admission = new AdmissionService(fixture.Runtime.Game, fixture.Composition, admissionUi);
    AdmissionResult waiting = admission.JoinAccountBacked(
        new ParticipantId(7007), new TrustedLocalIdentity("alice-local"));
    Assert.True(waiting.IsWaitingForCharacter);
    UiListRow alice = client.GetRows(waiting.CharacterSelectView, CharacterSelectUi.Characters)[0];
    client.SubmitAction(waiting.CharacterSelectView, CharacterSelectUi.Play, alice.Id);
    Assert.True(admission.TryGetSession(new ParticipantId(7007), out AdmissionResult entered));
    Assert.Equal(fixture.Character, entered.Entity);

    using var inventoryPort = new RecordingInventoryUiPort(link);
    using var inventory = new InventoryUiController(fixture.Runtime.Game, inventoryPort);
    InventoryUiOpenResult opened = inventory.Open(fixture.Backpack);
    Assert.True(opened.Succeeded);
    Assert.Equal(2, client.GetRows(opened.View, InventoryUi.Items).Length);
}

static void InventoryUiPublishesGranularRowDeltas()
{
    using Fixture fixture = CreateFixture();
    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    using var port = new RecordingInventoryUiPort(link);
    using var controller = new InventoryUiController(fixture.Runtime.Game, port);
    InventoryUiOpenResult opened = controller.Open(fixture.Backpack);
    Assert.True(opened.Succeeded);

    UiRowId bread = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Bread");
    client.SubmitAction(opened.View, InventoryUi.Select, bread);
    client.SubmitAction(opened.View, InventoryUi.Split);
    Assert.SequenceEqual(
        new[] { UiListDeltaKind.Update, UiListDeltaKind.Add },
        port.LastListKinds);
    Assert.False(port.LastListKinds.Contains(UiListDeltaKind.Replace));
    Assert.Equal(3, client.GetRows(opened.View, InventoryUi.Items).Length);

    UiRowId sword = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Iron Sword");
    client.SubmitAction(opened.View, InventoryUi.Select, sword);
    client.SubmitAction(opened.View, InventoryUi.Equip);
    Assert.SequenceEqual(new[] { UiListDeltaKind.Remove }, port.LastListKinds);
    Assert.False(port.LastListKinds.Contains(UiListDeltaKind.Replace));
    Assert.Equal(2, client.GetRows(opened.View, InventoryUi.Items).Length);
}

static void InventoryUiRejectsCrossWorldDurableIdentityAba()
{
    using Fixture fixture = CreateFixture();
    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    using var port = new RecordingInventoryUiPort(link);
    using var controller = new InventoryUiController(fixture.Runtime.Game, port);
    InventoryUiOpenResult opened = controller.Open(fixture.Backpack);
    UiRowId oldSword = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Iron Sword");
    client.SubmitAction(opened.View, InventoryUi.Select, oldSword);

    GameStaging staging = fixture.Composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x70_8008)));
    GameGenerationState? candidate = null;
    bool transferred = false;
    try
    {
        staging.World.Seal();
        staging.World.Initialize();
        staging.World.BeginRun();
        Assert.True(staging.World.TryInstantiate(staging.Content.Character, out Entity character));
        Assert.True(staging.Inventory.TryCreateOwnedContainer(
            staging.World, staging.Content.Backpack, character, 8, out Entity backpack));
        Assert.True(staging.Inventory.TryCreateItem(
            staging.World, backpack, staging.Content.Bread, 5, out _));
        Assert.True(staging.Inventory.TryCreateItem(
            staging.World, backpack, staging.Content.IronSword, 1, out Entity sword));
        Assert.True(staging.World.TryGetPersistentId(sword, out PersistentEntityId newSwordId));
        Assert.Equal(oldSword.Value, newSwordId.Value);

        candidate = new GameGenerationState(
            new AccountingStore(),
            staging.Inventory,
            staging.Content,
            staging.WorldState,
            staging.SimulationLookup);
        Assert.True(fixture.Runtime.Game.TryPublishPreparedGeneration(
            staging.World, candidate, out _));
        transferred = true;
        candidate = null;

        client.SubmitAction(opened.View, InventoryUi.Equip);

        Assert.Equal(0, controller.OpenViewCount);
        Assert.Equal(0, client.OpenViewCount);
        Assert.True(fixture.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                Assert.Equal(new PersistentWorldId(0x70_8008), world.PersistentWorldId);
                Assert.True(game.Inventory.TryReadItem(world, sword, out InventoryItem item));
                Assert.Equal(backpack, item.Parent);
                Assert.Equal(InventoryEquipSlot.None, item.EquipSlot);
                return true;
            }, out bool checkedState));
        Assert.True(checkedState);
    }
    finally
    {
        if (!transferred)
        {
            if (candidate is not null)
                candidate.Dispose();
            else
            {
                staging.WorldState.Dispose();
                staging.Inventory.Dispose();
            }
            if (!staging.World.IsDisposed)
                staging.World.Dispose();
        }
    }
}

static void InventoryUiDeliveryFailureDetachesAfterAuthoritativeCommit()
{
    using Fixture fixture = CreateFixture();
    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    using var port = new FaultingInventoryUiPort(link);
    using var controller = new InventoryUiController(fixture.Runtime.Game, port);
    int presentationFaults = 0;
    InventoryMutationResult completed = default;
    controller.PresentationFaulted += _ => presentationFaults++;
    controller.MutationCompleted += result => completed = result;
    InventoryUiOpenResult opened = controller.Open(fixture.Backpack);
    UiRowId bread = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Bread");
    client.SubmitAction(opened.View, InventoryUi.Select, bread);
    port.ThrowOnUpdate = true;

    client.SubmitAction(opened.View, InventoryUi.Split);

    Assert.True(completed.Accepted);
    Assert.Equal(3, fixture.Use((_, _, game) => game.Inventory.Index.ItemCount));
    Assert.Equal(0, controller.OpenViewCount);
    Assert.Equal(0, client.OpenViewCount);
    Assert.Equal(1, presentationFaults);
}

static void InventoryUiDoesNotConvertTerminalRuntimeFaultToDomainRejection()
{
    using Fixture fixture = CreateFixture();
    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    using var port = new RecordingInventoryUiPort(link);
    using var controller = new InventoryUiController(fixture.Runtime.Game, port);
    int completions = 0;
    controller.MutationCompleted += _ => completions++;
    InventoryUiOpenResult opened = controller.Open(fixture.Backpack);
    UiRowId bread = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Bread");
    client.SubmitAction(opened.View, InventoryUi.Select, bread);
    fixture.Use((_, _, game) => { game.Inventory.Dispose(); return true; });

    Assert.Throws<ObjectDisposedException>(() =>
        client.SubmitAction(opened.View, InventoryUi.Split));

    Assert.Equal(ServerRuntimeState.Faulted, fixture.Runtime.State);
    Assert.Equal(0, completions);
}

static void InventoryPersistsAndRebuildsAfterGenerationSwap()
{
    using Fixture fixture = CreateFixture();
    GameSaveResult save = fixture.Persistence.TrySave(new SaveSetContext(1, 1), static capture => capture());
    if (!save.Succeeded)
        throw new InvalidOperationException("The game save failed.", save.Failure);
    Assert.Equal(3, save.Manifest!.Artifacts.Length);
    EcsWorld oldWorld = fixture.CurrentWorld;
    Assert.True(fixture.Use((world, _, game) => game.Inventory.Equip(
        world, fixture.Sword, fixture.Backpack, fixture.Character, InventoryEquipSlot.MainHand).Accepted));

    GameLoadResult load = fixture.Persistence.TryLoad();

    Assert.True(load.Succeeded);
    Assert.True(oldWorld.IsDisposed);
    fixture.Use((world, _, game) =>
    {
        Assert.True(world.TryResolvePersistentEntity(fixture.CharacterId, out Entity character));
        Assert.True(world.TryResolvePersistentEntity(fixture.BackpackId, out Entity backpack));
        Assert.True(world.TryResolvePersistentEntity(fixture.BreadId, out Entity bread));
        Assert.True(world.TryResolvePersistentEntity(fixture.SwordId, out Entity sword));
        Assert.True(game.Inventory.TryGetContainerOwner(world, backpack, out Entity owner));
        Assert.Equal(character, owner);
        Assert.Equal(backpack, ReadItem(world, game.Inventory, bread).Parent);
        Assert.Equal(backpack, ReadItem(world, game.Inventory, sword).Parent);
        Assert.Equal(2, game.Inventory.Index.ItemCount);
        Assert.True(game.Accounting.TryGetCharacter(new CharacterId(1), out CharacterRecord alice));
        Assert.Equal(fixture.CharacterId, alice.PersistentEntityKey.Entity);
        return true;
    });
}

static void EquippedRelationshipPersistsAndReindexes()
{
    using Fixture fixture = CreateFixture();
    Assert.True(fixture.Use((world, _, game) => game.Inventory.Equip(
        world, fixture.Sword, fixture.Backpack, fixture.Character, InventoryEquipSlot.MainHand).Accepted));
    Assert.True(fixture.Persistence.TrySave(new SaveSetContext(1, 1), static capture => capture()).Succeeded);
    Assert.True(fixture.Persistence.TryLoad().Succeeded);
    fixture.Use((world, _, game) =>
    {
        Assert.True(world.TryResolvePersistentEntity(fixture.CharacterId, out Entity character));
        Assert.True(world.TryResolvePersistentEntity(fixture.BackpackId, out Entity backpack));
        Assert.True(world.TryResolvePersistentEntity(fixture.SwordId, out Entity sword));
        InventoryItem restored = ReadItem(world, game.Inventory, sword);
        Assert.Equal(character, restored.Parent);
        Assert.Equal(InventoryEquipSlot.MainHand, restored.EquipSlot);
        Assert.True(game.Inventory.Index.TryGetEquipped(
            character, InventoryEquipSlot.MainHand, out Entity indexed));
        Assert.Equal(sword, indexed);
        Assert.Equal(1, game.Inventory.Index.GetItems(backpack).Count);
        return true;
    });
}

static void FailedInventoryRebuildDoesNotPublishCandidate()
{
    using Fixture fixture = CreateFixture();
    Assert.True(fixture.Persistence.TrySave(new SaveSetContext(1, 1), static capture => capture()).Succeeded);
    EcsWorld active = fixture.CurrentWorld;
    ulong generation = fixture.Use((_, id, _) => id);
    var constrained = new GameRuntimePersistence(
        fixture.Runtime.Game,
        fixture.Composition,
        RestoreOptions(),
        new SaveSetCoordinator(fixture.Root),
        stagingOverride: requirements => fixture.Composition.CreateStaging(
            requirements, new InventoryLimits(16, 1, 1)));

    GameLoadResult load = constrained.TryLoad();

    Assert.False(load.Succeeded);
    Assert.True(load.Failure is InvalidOperationException);
    Assert.True(ReferenceEquals(active, fixture.CurrentWorld));
    Assert.False(active.IsDisposed);
    Assert.Equal(generation, fixture.Use((_, id, _) => id));
    Assert.Equal(2, fixture.Use((_, _, game) => game.Inventory.Index.ItemCount));
}

static void InventoryUiLifetimeIsOwnerBoundAndIdempotent()
{
    using Fixture fixture = CreateFixture();
    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    using var port = new RecordingInventoryUiPort(link);
    var controller = new InventoryUiController(fixture.Runtime.Game, port);
    Assert.True(controller.Open(fixture.Backpack).Succeeded);
    InventoryIndex index = fixture.Use((_, _, game) => game.Inventory.Index);
    Exception? controllerFailure = null;
    Exception? indexFailure = null;
    var thread = new Thread(() =>
    {
        try { _ = controller.OpenViewCount; }
        catch (Exception exception) { controllerFailure = exception; }
        try { _ = index.ItemCount; }
        catch (Exception exception) { indexFailure = exception; }
    });
    thread.Start();
    thread.Join();
    Assert.True(controllerFailure is InvalidOperationException);
    Assert.True(indexFailure is InvalidOperationException);
    controller.Dispose();
    controller.Dispose();
    Assert.Equal(0, client.OpenViewCount);
}

static void InventoryIndexBenchmarksDescribeMeasuredWork()
{
    using var benchmark = new InventoryBenchmarkFixture(1_000);
    CollectGarbage();
    Measurement resolved = MeasureResolvedChildScan(benchmark, rows: 1_000);
    Measurement lookup = MeasureLookupAndChildScan(benchmark, rows: 1_000);
    Measurement hotMove = MeasureHotPingPongMove(benchmark);
    Assert.True(resolved.Checksum != 0 && lookup.Checksum != 0 && hotMove.Checksum != 0);
    Assert.Equal(0L, resolved.Allocation);
    Assert.Equal(0L, lookup.Allocation);
    Assert.Equal(0L, hotMove.Allocation);
    Console.WriteLine(
        $"BASELINE S0.7 resolved_child_scan_1000_mean={resolved.Mean:F2}ns " +
        $"resolved_child_scan_1000_p95={resolved.P95:F2}ns " +
        $"lookup_and_child_scan_1000_mean={lookup.Mean:F2}ns " +
        $"lookup_and_child_scan_1000_p95={lookup.P95:F2}ns " +
        $"hot_same_item_two_container_pingpong_mean={hotMove.Mean:F2}ns " +
        $"hot_same_item_two_container_pingpong_p95={hotMove.P95:F2}ns alloc=0B");
}

static Measurement MeasureResolvedChildScan(InventoryBenchmarkFixture fixture, int rows)
{
    InventoryItemCollection items = fixture.State.Index.GetItems(fixture.Source);
    return Measure(() =>
    {
        long checksum = 0;
        int visited = 0;
        foreach (Entity entity in items)
        {
            checksum = unchecked(checksum + entity.Index);
            if (++visited == rows)
                break;
        }
        return checksum;
    });
}

static Measurement MeasureLookupAndChildScan(InventoryBenchmarkFixture fixture, int rows) => Measure(() =>
{
    long checksum = 0;
    int visited = 0;
    foreach (Entity entity in fixture.State.Index.GetItems(fixture.Source))
    {
        checksum = unchecked(checksum + entity.Index);
        if (++visited == rows)
            break;
    }
    return checksum;
});

static Measurement MeasureHotPingPongMove(InventoryBenchmarkFixture fixture)
{
    Entity moved = fixture.Items[^1];
    int direction = 0;
    return Measure(() =>
    {
        Entity source = direction == 0 ? fixture.Source : fixture.Destination;
        Entity destination = direction == 0 ? fixture.Destination : fixture.Source;
        direction ^= 1;
        InventoryMutationResult result = fixture.State.Move(fixture.World, moved, source, destination);
        if (!result.Accepted)
            throw new InvalidOperationException("The hot inventory move control failed.");
        return unchecked((long)moved.Index + direction);
    });
}

static Measurement Measure(Func<long> operation)
{
    const int warmup = 1_000;
    const int operations = 20_000;
    const int samples = 21;
    long checksum = 0;
    for (int index = 0; index < warmup; index++)
        checksum = unchecked(checksum + operation());
    var elapsed = new double[samples];
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < samples; sample++)
    {
        long started = Stopwatch.GetTimestamp();
        for (int index = 0; index < operations; index++)
            checksum = unchecked(checksum + operation());
        elapsed[sample] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / operations;
    }
    long allocation = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Array.Sort(elapsed);
    return new Measurement(elapsed.Average(), elapsed[(int)Math.Ceiling(samples * 0.95) - 1], checksum, allocation);
}

static InventoryItem ReadItem(EcsWorld world, InventoryGenerationState state, Entity entity)
{
    Assert.True(state.TryReadItem(world, entity, out InventoryItem item));
    return item;
}

static UiRowId FindRow(ReadOnlySpan<UiListRow> rows, string displayName)
{
    foreach (UiListRow row in rows)
    {
        if (row.Values.Length != 0 && row.Values[0].Text == displayName)
            return row.Id;
    }
    throw new InvalidOperationException($"The inventory row '{displayName}' was not found.");
}

static PersistenceRestoreOptions RestoreOptions() => new(
    new PersistenceLimits(1_000_000, 64, 32, 32, 128, 1_000_000, 1_000_000),
    ChunkLayoutOptions.KiB16);

static Fixture CreateFixture()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-s0-7-" + Guid.NewGuid().ToString("N"));
    var composition = new UltraSimGameComposition(new InventoryLimits(64, 128, 16));
    var runtime = new ServerRuntime(
        composition,
        new ServerRuntimeOptions(
            UltraSimGameComposition.CreateWorldOptions(
                256,
                new PersistenceIdentityOptions(new PersistentWorldId(0x7007))),
            WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();
    Assert.True(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, FixtureEntities>(
        (world, _, game) =>
        {
            Assert.True(world.TryInstantiate(game.Content.Character, out Entity character));
            Assert.True(game.Inventory.TryCreateOwnedContainer(
                world, game.Content.Backpack, character, 8, out Entity backpack));
            Assert.True(game.Inventory.TryCreateItem(world, backpack, game.Content.Bread, 5, out Entity bread));
            Assert.True(game.Inventory.TryCreateItem(world, backpack, game.Content.IronSword, 1, out Entity sword));
            Assert.True(world.TryGetPersistentId(character, out PersistentEntityId characterId));
            Assert.True(world.TryGetPersistentId(backpack, out PersistentEntityId backpackId));
            Assert.True(world.TryGetPersistentId(bread, out PersistentEntityId breadId));
            Assert.True(world.TryGetPersistentId(sword, out PersistentEntityId swordId));
            Assert.True(world.TryGetPrefabProvenance(character, out PrefabKey characterPrefab));
            var accountId = new AccountId(1);
            game.Accounting.AddAccount(new AccountRecord(accountId));
            game.Accounting.AddCharacter(new CharacterRecord(
                new CharacterId(1), accountId, 0, "Alice", characterPrefab,
                new PersistentEntityKey(world.PersistentWorldId, characterId)));
            game.Accounting.BindTrustedIdentity(new TrustedLocalIdentity("alice-local"), accountId);
            return new FixtureEntities(
                character, backpack, bread, sword,
                characterId, backpackId, breadId, swordId);
        }, out FixtureEntities entities));
    var persistence = new GameRuntimePersistence(
        runtime.Game,
        composition,
        RestoreOptions(),
        new SaveSetCoordinator(root));
    return new Fixture(root, runtime, composition, persistence, entities);
}

static void CollectGarbage()
{
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
}

readonly record struct Measurement(double Mean, double P95, long Checksum, long Allocation);

readonly record struct FixtureEntities(
    Entity Character,
    Entity Backpack,
    Entity Bread,
    Entity Sword,
    PersistentEntityId CharacterId,
    PersistentEntityId BackpackId,
    PersistentEntityId BreadId,
    PersistentEntityId SwordId);

sealed class Fixture : IDisposable
{
    private readonly FixtureEntities _entities;

    internal Fixture(
        string root,
        ServerRuntime runtime,
        UltraSimGameComposition composition,
        GameRuntimePersistence persistence,
        FixtureEntities entities)
    {
        Root = root;
        Runtime = runtime;
        Composition = composition;
        Persistence = persistence;
        _entities = entities;
    }

    internal string Root { get; }
    internal ServerRuntime Runtime { get; }
    internal UltraSimGameComposition Composition { get; }
    internal GameRuntimePersistence Persistence { get; }
    internal Entity Character => _entities.Character;
    internal Entity Backpack => _entities.Backpack;
    internal Entity Bread => _entities.Bread;
    internal Entity Sword => _entities.Sword;
    internal PersistentEntityId CharacterId => _entities.CharacterId;
    internal PersistentEntityId BackpackId => _entities.BackpackId;
    internal PersistentEntityId BreadId => _entities.BreadId;
    internal PersistentEntityId SwordId => _entities.SwordId;

    internal EcsWorld CurrentWorld
    {
        get
        {
            Assert.True(Runtime.Game.TryUseActiveGeneration(static (world, _) => world, out EcsWorld world));
            return world;
        }
    }

    internal TResult Use<TResult>(ServerGenerationStateOperation<IGameGenerationStateProvider, TResult> operation)
    {
        Assert.True(Runtime.Game.TryUseActiveGeneration(operation, out TResult result));
        return result;
    }

    public void Dispose()
    {
        Runtime.Dispose();
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

sealed class RecordingInventoryUiPort : IUiSemanticPort, IDisposable
{
    private readonly LocalSemanticLink _link;
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];

    internal RecordingInventoryUiPort(LocalSemanticLink link)
    {
        _link = link;
        _link.ActionSubmitted += Forward;
    }
    internal UiListDeltaKind[] LastListKinds { get; private set; } = [];

    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
        _actions.Add(instance, handler);
    public void UnbindActions(UiViewInstanceId instance) => _actions.Remove(instance);

    public UiViewInstanceId OpenView(UiOpenView operation) => _link.OpenView(operation);
    public UiViewInstanceId OpenHotView(UiHotOpenView operation) => _link.OpenHotView(operation);
    public void UpdateView(UiUpdateView operation)
    {
        LastListKinds = operation.Delta.Lists.ToArray().Select(static patch => patch.Kind).ToArray();
        _link.UpdateView(operation);
    }
    public bool CloseView(UiCloseView operation) => _link.CloseView(operation);
    public void Dispose() { _link.ActionSubmitted -= Forward; _actions.Clear(); }
    private void Forward(UiActionRequest request)
    {
        if (_actions.TryGetValue(request.Instance, out Action<UiActionRequest>? handler)) handler(request);
    }
}

sealed class FaultingInventoryUiPort : IUiSemanticPort, IDisposable
{
    private readonly LocalSemanticLink _link;
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];

    internal FaultingInventoryUiPort(LocalSemanticLink link)
    {
        _link = link;
        _link.ActionSubmitted += Forward;
    }
    internal bool ThrowOnUpdate { get; set; }

    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
        _actions.Add(instance, handler);
    public void UnbindActions(UiViewInstanceId instance) => _actions.Remove(instance);

    public UiViewInstanceId OpenView(UiOpenView operation) => _link.OpenView(operation);
    public UiViewInstanceId OpenHotView(UiHotOpenView operation) => _link.OpenHotView(operation);
    public void UpdateView(UiUpdateView operation)
    {
        if (ThrowOnUpdate) throw new InvalidOperationException("Injected UI delivery failure.");
        _link.UpdateView(operation);
    }
    public bool CloseView(UiCloseView operation) => _link.CloseView(operation);
    public void Dispose() { _link.ActionSubmitted -= Forward; _actions.Clear(); }
    private void Forward(UiActionRequest request)
    {
        if (_actions.TryGetValue(request.Instance, out Action<UiActionRequest>? handler)) handler(request);
    }
}

sealed class InventoryBenchmarkFixture : IDisposable
{
    private readonly GameStaging _staging;

    internal InventoryBenchmarkFixture(int population)
    {
        var limits = new InventoryLimits(4, population + 16, population);
        var composition = new UltraSimGameComposition(limits, maximumChunksPerContentArchetype: 32);
        _staging = composition.CreateFresh(
            population + 16,
            new PersistenceIdentityOptions(new PersistentWorldId(0x70_1717)),
            limits);
        World = _staging.World;
        State = _staging.Inventory;
        World.Seal();
        World.Initialize();
        World.BeginRun();
        Assert.True(State.TryCreateContainer(World, _staging.Content.WorldChest, population, out Entity source));
        Assert.True(State.TryCreateContainer(World, _staging.Content.WorldChest, population, out Entity destination));
        Source = source;
        Destination = destination;
        Items = new Entity[population];
        for (int index = 0; index < population; index++)
            Assert.True(State.TryCreateItem(World, Source, _staging.Content.Bread, 1, out Items[index]));
    }

    internal EcsWorld World { get; }
    internal InventoryGenerationState State { get; }
    internal Entity Source { get; }
    internal Entity Destination { get; }
    internal Entity[] Items { get; }

    public void Dispose()
    {
        State.Dispose();
        World.Dispose();
    }
}

sealed class MemoryRenderer : IUiRenderer
{
    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction) => new Handle();

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction) { }

    public void Close(IUiRenderHandle handle) { }
    private sealed class Handle : IUiRenderHandle;
}

static class Assert
{
    internal static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    internal static void False(bool value)
    {
        if (value) throw new InvalidOperationException("Expected false.");
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    internal static void SequenceEqual<T>(T[] expected, T[] actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Expected sequences to match.");
    }

    internal static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
