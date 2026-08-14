using UltraSim.Client.Core;
using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Inventory;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("FixtureRebuildsBoundedIndexFromEcsTruth", FixtureRebuildsBoundedIndexFromEcsTruth),
    ("CompiledPrefabCreationMaintainsIndexImmediately", CompiledPrefabCreationMaintainsIndexImmediately),
    ("MoveSplitEquipAreAtomicAndPreventLoss", MoveSplitEquipAreAtomicAndPreventLoss),
    ("PackedIndexRemovalRepairsDonorRows", PackedIndexRemovalRepairsDonorRows),
    ("ProjectionOrderIsStableAcrossRebuild", ProjectionOrderIsStableAcrossRebuild),
    ("FailedRebuildPreservesPublishedIndex", FailedRebuildPreservesPublishedIndex),
    ("LimitsAndExpectedRejectionsDoNotMutateEcs", LimitsAndExpectedRejectionsDoNotMutateEcs),
    ("AccountingAdmissionAndInventoryShareOneGeneration", AccountingAdmissionAndInventoryShareOneGeneration),
    ("InventoryGumpUsesDeltasForSplitAndEquip", InventoryGumpUsesDeltasForSplitAndEquip),
    ("InventoryPersistsThroughEcsAndRebuildsAfterGenerationSwap", InventoryPersistsThroughEcsAndRebuildsAfterGenerationSwap),
    ("EquippedRelationshipPersistsAndReindexes", EquippedRelationshipPersistsAndReindexes),
    ("FailedInventoryRebuildDoesNotPublishCandidateGeneration", FailedInventoryRebuildDoesNotPublishCandidateGeneration),
    ("InventoryUiLifetimeIsIdempotentAndOwnerBound", InventoryUiLifetimeIsIdempotentAndOwnerBound),
    ("InventoryUiCloseFailurePreservesOwnership", InventoryUiCloseFailurePreservesOwnership),
    ("InventoryIndexBaselinesAreCaptured", InventoryIndexBaselinesAreCaptured),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} S0.7 Game Inventory tests passed.");

static void FixtureRebuildsBoundedIndexFromEcsTruth()
{
    using Fixture fixture = CreateFixture();
    InventoryItemView[] items = fixture.Use((world, _, state) =>
        state.Snapshot(world, fixture.Backpack).ToArray());

    Assert.Equal(1, fixture.Use((_, _, state) => state.Index.ContainerCount));
    Assert.Equal(2, fixture.Use((_, _, state) => state.Index.ItemCount));
    Assert.Equal(2, items.Length);
    Assert.Equal(InventoryItemKind.Bread, items[0].Kind);
    Assert.Equal(5, items[0].Quantity);
    Assert.Equal(InventoryItemKind.IronSword, items[1].Kind);
    Assert.Equal(1, items[1].Quantity);

    IReadOnlyList<Entity> indexed = fixture.Use((_, _, state) => state.Index.GetItems(fixture.Backpack));
    Assert.Equal(2, indexed.Count);
}

static void CompiledPrefabCreationMaintainsIndexImmediately()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, state) =>
    {
        Assert.True(world.TryGetPrefabProvenance(fixture.Character, out PrefabKey character));
        Assert.True(world.TryGetPrefabProvenance(fixture.Backpack, out PrefabKey backpack));
        Assert.True(world.TryGetPrefabProvenance(fixture.Bread, out PrefabKey bread));
        Assert.True(world.TryGetPrefabProvenance(fixture.Sword, out PrefabKey sword));
        Assert.Equal(InventoryPrefabKeys.Character, character);
        Assert.Equal(InventoryPrefabKeys.Backpack, backpack);
        Assert.Equal(InventoryPrefabKeys.Bread, bread);
        Assert.Equal(InventoryPrefabKeys.IronSword, sword);

        Assert.True(state.TryCreateContainer(world, fixture.Character, 4, out Entity pouch));
        Assert.True(state.Index.ContainsContainer(pouch));
        Assert.True(state.TryCreateItem(world, pouch, InventoryItemKind.Bread, 3, out Entity item));
        Assert.True(state.Index.TryGetLocation(item, out InventoryLocation location));
        Assert.Equal(pouch, location.Parent);
        Assert.Equal(1, state.Index.GetItems(pouch).Count);
        Assert.Equal(3, state.Index.ItemCount);
        return true;
    });
}

static void MoveSplitEquipAreAtomicAndPreventLoss()
{
    using Fixture fixture = CreateFixture();
    Entity pouch = fixture.Use((world, _, state) =>
    {
        Assert.True(state.TryCreateContainer(world, fixture.Character, 4, out Entity created));
        Assert.True(state.Index.ContainsContainer(created));
        return created;
    });

    InventoryMutationResult moved = fixture.Use((world, _, state) =>
        state.Move(world, fixture.Bread, fixture.Backpack, pouch));
    Assert.True(moved.Accepted);

    InventoryMutationResult wrongSource = fixture.Use((world, _, state) =>
        state.Move(world, fixture.Bread, fixture.Backpack, pouch));
    Assert.Equal(InventoryMutationStatus.WrongSource, wrongSource.Status);
    Assert.Equal(pouch, fixture.ReadItem(fixture.Bread).Parent);

    InventoryMutationResult split = fixture.Use((world, _, state) =>
        state.Split(world, fixture.Bread, pouch, fixture.Backpack, 2));
    Assert.True(split.Accepted);
    Assert.True(split.CreatedItem.IsValid);

    InventoryMutationResult invalidQuantity = fixture.Use((world, _, state) =>
        state.Split(world, fixture.Bread, pouch, fixture.Backpack, 0));
    Assert.Equal(InventoryMutationStatus.InvalidQuantity, invalidQuantity.Status);

    InventoryMutationResult equipped = fixture.Use((world, _, state) =>
        state.Equip(
            world,
            fixture.Sword,
            fixture.Backpack,
            fixture.Character,
            InventoryEquipSlot.MainHand));
    Assert.True(equipped.Accepted);

    InventoryMutationResult occupied = fixture.Use((world, _, state) =>
        state.Equip(
            world,
            split.CreatedItem,
            fixture.Backpack,
            fixture.Character,
            InventoryEquipSlot.MainHand));
    Assert.Equal(InventoryMutationStatus.SlotOccupied, occupied.Status);

    InventoryMutationResult missingContainer = fixture.Use((world, _, state) =>
        state.Move(world, split.CreatedItem, fixture.Backpack, Entity.Null));
    Assert.Equal(InventoryMutationStatus.MissingContainer, missingContainer.Status);

    InventoryMutationResult destroyed = fixture.Use((world, _, state) =>
        state.DestroyItem(world, fixture.Sword));
    Assert.True(destroyed.Accepted);
    InventoryMutationResult stale = fixture.Use((world, _, state) =>
        state.Move(world, fixture.Sword, fixture.Backpack, pouch));
    Assert.Equal(InventoryMutationStatus.MissingItem, stale.Status);

    Assert.Equal(2, fixture.Use((_, _, state) => state.Index.ItemCount));
    Assert.False(fixture.Use((_, _, state) =>
        state.Index.TryGetEquipped(fixture.Character, InventoryEquipSlot.MainHand, out _)));
    InventoryItem bread = fixture.ReadItem(fixture.Bread);
    InventoryItem splitBread = fixture.ReadItem(split.CreatedItem);
    Assert.Equal(3, bread.Quantity);
    Assert.Equal(2, splitBread.Quantity);
}

static void LimitsAndExpectedRejectionsDoNotMutateEcs()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, state) =>
    {
        int aliveBefore = world.AliveCount;
        ChangeVersion changeBefore = world.CurrentChangeVersion;
        Assert.False(state.TryCreateContainer(world, fixture.Character, 17, out Entity oversized));
        Assert.False(oversized.IsValid);
        Assert.Equal(aliveBefore, world.AliveCount);
        Assert.Equal(changeBefore, world.CurrentChangeVersion);

        Assert.True(state.TryCreateContainer(world, fixture.Character, 16, out Entity full));
        Assert.True(state.TryCreateContainer(world, fixture.Character, 16, out Entity reserveTwo));
        Assert.True(state.TryCreateContainer(world, fixture.Character, 16, out Entity reserveThree));
        aliveBefore = world.AliveCount;
        changeBefore = world.CurrentChangeVersion;
        Assert.False(state.TryCreateContainer(world, fixture.Character, 16, out Entity reservationOverflow));
        Assert.False(reservationOverflow.IsValid);
        Assert.Equal(aliveBefore, world.AliveCount);
        Assert.Equal(changeBefore, world.CurrentChangeVersion);
        for (int index = 0; index < 16; index++)
            Assert.True(state.TryCreateItem(world, full, InventoryItemKind.Bread, 2, out Entity _created));
        aliveBefore = world.AliveCount;
        changeBefore = world.CurrentChangeVersion;
        Assert.False(state.TryCreateItem(world, full, InventoryItemKind.Bread, 1, out Entity overflow));
        Assert.False(overflow.IsValid);
        Assert.Equal(aliveBefore, world.AliveCount);
        Assert.Equal(changeBefore, world.CurrentChangeVersion);

        InventoryMutationResult invalidSplit = state.Split(
            world,
            fixture.Sword,
            fixture.Backpack,
            fixture.Backpack,
            quantity: 1);
        Assert.Equal(InventoryMutationStatus.InvalidItemKind, invalidSplit.Status);
        Assert.Equal(aliveBefore, world.AliveCount);
        Assert.Equal(changeBefore, world.CurrentChangeVersion);

        InventoryMutationResult nonEmptyDestroy = state.DestroyContainer(world, fixture.Backpack);
        Assert.Equal(InventoryMutationStatus.ContainerNotEmpty, nonEmptyDestroy.Status);
        Assert.True(world.IsAlive(fixture.Backpack));
        return true;
    });
}

static void PackedIndexRemovalRepairsDonorRows()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, state) =>
    {
        Assert.True(state.TryCreateContainer(world, fixture.Character, 8, out Entity pouch));
        Assert.True(state.TryCreateItem(world, pouch, InventoryItemKind.Bread, 2, out Entity first));
        Assert.True(state.TryCreateItem(world, pouch, InventoryItemKind.Bread, 2, out Entity middle));
        Assert.True(state.TryCreateItem(world, pouch, InventoryItemKind.Bread, 2, out Entity donor));
        Assert.True(state.DestroyItem(world, middle).Accepted);
        Assert.True(state.Index.TryGetLocation(donor, out InventoryLocation repaired));
        Assert.Equal(1, repaired.ContainerRow);
        Assert.True(state.Move(world, donor, pouch, fixture.Backpack).Accepted);
        Assert.True(state.Index.TryGetLocation(first, out InventoryLocation retained));
        Assert.Equal(0, retained.ContainerRow);
        Assert.Equal(1, state.Index.GetItems(pouch).Count);

        Assert.True(state.Move(world, first, pouch, fixture.Backpack).Accepted);
        Assert.True(state.DestroyContainer(world, pouch).Accepted);
        Assert.False(state.Index.ContainsContainer(pouch));
        InventoryMutationResult staleContainer = state.Move(
            world,
            fixture.Bread,
            fixture.Backpack,
            pouch);
        Assert.Equal(InventoryMutationStatus.MissingContainer, staleContainer.Status);
        return true;
    });
}

static void ProjectionOrderIsStableAcrossRebuild()
{
    using Fixture fixture = CreateFixture();
    fixture.Use((world, _, state) =>
    {
        Assert.True(state.TryCreateContainer(world, fixture.Character, 8, out Entity pouch));
        Assert.True(state.Move(world, fixture.Sword, fixture.Backpack, pouch).Accepted);
        Assert.True(state.Move(world, fixture.Bread, fixture.Backpack, pouch).Accepted);
        PersistentEntityId[] before = state.Snapshot(world, pouch)
            .Select(static item => item.PersistentId)
            .ToArray();
        state.Rebuild(world);
        PersistentEntityId[] after = state.Snapshot(world, pouch)
            .Select(static item => item.PersistentId)
            .ToArray();
        Assert.True(before.AsSpan().SequenceEqual(after));
        return true;
    });
}

static void FailedRebuildPreservesPublishedIndex()
{
    using var fixture = new InventoryBenchmarkFixture(10);
    {
        Entity[] beforeRows = fixture.State.Index.GetItems(fixture.Source).ToArray();
        InventoryIndexSnapshot beforeSnapshot = fixture.State.Index.CaptureSnapshot();
        Entity item = fixture.Items[0];
        Assert.True(fixture.World.TryRead(item, fixture.ItemLookup, out InventoryItem original));
        InventoryItem corrupt = original;
        corrupt.Parent = Entity.Null;
        Assert.True(fixture.World.TryWrite(item, fixture.ItemLookup, corrupt));

        Assert.Throws<InvalidOperationException>(() => fixture.State.Rebuild(fixture.World));
        InventoryIndexSnapshot afterFailure = fixture.State.Index.CaptureSnapshot();
        Assert.Equal(beforeSnapshot, afterFailure);
        Assert.True(beforeRows.AsSpan().SequenceEqual(fixture.State.Index.GetItems(fixture.Source).ToArray()));

        Assert.True(fixture.World.TryWrite(item, fixture.ItemLookup, original));
        fixture.State.Rebuild(fixture.World);
        Assert.True(beforeRows.AsSpan().SequenceEqual(fixture.State.Index.GetItems(fixture.Source).ToArray()));
    }
}

static void AccountingAdmissionAndInventoryShareOneGeneration()
{
    using Fixture fixture = CreateFixture();
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    using var admissionUi = new LocalAdmissionUiPort(link);
    using var admission = new AdmissionService(fixture.Runtime.Game, fixture.Composition, admissionUi);

    AdmissionResult waiting = admission.JoinAccountBacked(
        new ParticipantId(7007),
        new TrustedLocalIdentity("alice-local"));
    Assert.True(waiting.IsWaitingForCharacter);
    ReadOnlySpan<UiListRow> characters = client.GetRows(
        waiting.CharacterSelectView,
        CharacterSelectUi.Characters);
    Assert.Equal(1, characters.Length);
    client.SubmitAction(waiting.CharacterSelectView, CharacterSelectUi.Play, characters[0].Id);
    Assert.True(admission.TryGetSession(new ParticipantId(7007), out AdmissionResult entered));
    Assert.True(entered.IsEntered);
    Assert.Equal(fixture.Character, entered.Entity);

    using var inventoryPort = new InventoryUiPort(link);
    using var inventory = new InventoryUiController(fixture.Runtime.Game, inventoryPort);
    InventoryUiOpenResult opened = inventory.Open(fixture.Backpack);
    Assert.True(opened.Succeeded);
    Assert.Equal(2, client.GetRows(opened.View, InventoryUi.Items).Length);
}

static void InventoryGumpUsesDeltasForSplitAndEquip()
{
    using Fixture fixture = CreateFixture();
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    using var port = new InventoryUiPort(link);
    using var controller = new InventoryUiController(fixture.Runtime.Game, port);

    InventoryUiOpenResult opened = controller.Open(fixture.Backpack);
    Assert.True(opened.Succeeded);
    Assert.Equal(1, renderer.CreateCount);
    Assert.Equal(1, controller.OpenViewCount);
    Assert.Equal(2, client.GetRows(opened.View, InventoryUi.Items).Length);
    Assert.Equal(1, InventoryUi.Definition.Elements.ToArray().Count(static element => element.Kind == UiElementKind.List));
    Assert.Equal(2, InventoryUi.Definition.Elements.ToArray().Count(static element => element.Kind == UiElementKind.Button));

    Assert.Throws<InvalidOperationException>(() => client.SubmitAction(opened.View, InventoryUi.Equip));

    client.SubmitAction(opened.View, InventoryUi.Select, new UiRowId(ulong.MaxValue));
    Assert.Equal("Rejected: The selected inventory row is stale.", client.GetValue(opened.View, InventoryUi.Status).Text);
    Assert.Throws<InvalidOperationException>(() => client.SubmitAction(opened.View, InventoryUi.Split));

    UiRowId swordRow = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Iron Sword");
    client.SubmitAction(opened.View, InventoryUi.Select, swordRow);
    Assert.Equal("Item selected.", client.GetValue(opened.View, InventoryUi.Status).Text);
    client.SubmitAction(opened.View, InventoryUi.Equip);
    Assert.Equal(3, renderer.ApplyCount);
    Assert.Equal(1, controller.OpenViewCount);
    Assert.Equal("Equip accepted.", client.GetValue(opened.View, InventoryUi.Status).Text);
    Assert.Equal(1, client.GetRows(opened.View, InventoryUi.Items).Length);

    UiRowId breadRow = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Bread");
    client.SubmitAction(opened.View, InventoryUi.Select, breadRow);
    client.SubmitAction(opened.View, InventoryUi.Split);
    Assert.Equal(5, renderer.ApplyCount);
    Assert.Equal(2, client.GetRows(opened.View, InventoryUi.Items).Length);
    Assert.Equal("Split accepted.", client.GetValue(opened.View, InventoryUi.Status).Text);
}

static void InventoryPersistsThroughEcsAndRebuildsAfterGenerationSwap()
{
    using Fixture fixture = CreateFixture();
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    using var port = new InventoryUiPort(link);
    using var controller = new InventoryUiController(fixture.Runtime.Game, port);
    InventoryUiOpenResult initial = controller.Open(fixture.Backpack);
    Assert.True(initial.Succeeded);
    Assert.True(controller.Close(initial.View));

    InventorySaveResult save = fixture.Persistence.TrySave(
        new SaveSetContext(1, 1),
        static capture => capture());
    Assert.True(save.Succeeded);
    Assert.Equal(2, save.Manifest!.Artifacts.Length);
    Assert.Equal("accounting", save.Manifest.Artifacts[0].Key);
    Assert.Equal("world", save.Manifest.Artifacts[1].Key);

    EcsWorld oldWorld = fixture.CurrentWorld;
    InventoryMutationResult mutated = fixture.Use((world, _, state) =>
        state.Equip(
            world,
            fixture.Sword,
            fixture.Backpack,
            fixture.Character,
            InventoryEquipSlot.MainHand));
    Assert.True(mutated.Accepted);

    InventoryLoadResult loaded = fixture.Persistence.TryLoad();
    Assert.True(loaded.Succeeded);
    Assert.Equal(2UL, loaded.GenerationId);
    Assert.True(oldWorld.IsDisposed);
    Assert.Throws<ObjectDisposedException>(() => _ = oldWorld.IsAlive(fixture.Sword));

    InventoryItemView[] restored = fixture.Use((world, _, state) =>
    {
        Assert.True(world.TryResolvePersistentEntity(fixture.CharacterId, out Entity character));
        Assert.True(world.TryResolvePersistentEntity(fixture.BackpackId, out Entity backpack));
        Assert.True(world.TryResolvePersistentEntity(fixture.BreadId, out Entity bread));
        Assert.True(world.TryResolvePersistentEntity(fixture.SwordId, out Entity sword));
        Assert.True(state.TryGetContainerOwner(world, backpack, out Entity owner));
        Assert.Equal(character, owner);
        Assert.True(state.TryReadItem(world, bread, out InventoryItem breadValue));
        Assert.True(state.TryReadItem(world, sword, out InventoryItem swordValue));
        Assert.Equal(backpack, breadValue.Parent);
        Assert.Equal(backpack, swordValue.Parent);
        return state.Snapshot(world, backpack).ToArray();
    });
    Assert.Equal(2, restored.Length);
    Assert.Equal(InventoryItemKind.Bread, restored[0].Kind);
    Assert.Equal(5, restored[0].Quantity);
    Assert.Equal(InventoryItemKind.IronSword, restored[1].Kind);
    Assert.Equal(InventoryEquipSlot.None, restored[1].EquipSlot);
    Assert.Equal(2, fixture.Use((_, _, state) => state.Index.ItemCount));

    Entity restoredBackpack = fixture.Use((world, _, _) =>
    {
        Assert.True(world.TryResolvePersistentEntity(fixture.BackpackId, out Entity backpack));
        return backpack;
    });
    InventoryUiOpenResult reopened = controller.OpenHot(restoredBackpack);
    Assert.True(reopened.Succeeded);
    Assert.Equal(2, client.GetRows(reopened.View, InventoryUi.Items).Length);
    Assert.Equal(2, renderer.CreateCount);
    Assert.Equal(1, port.HotOpenCount);

    Assert.True(fixture.Runtime.Game.TryUseActiveGeneration<InventoryGameGenerationState, bool>(
        (world, _, game) =>
        {
            Assert.True(game.Accounting.TryGetCharacter(new CharacterId(1), out CharacterRecord alice));
            Assert.Equal(fixture.CharacterId, alice.PersistentEntityKey.Entity);
            Assert.True(world.TryResolvePersistentEntity(alice.PersistentEntityKey.Entity, out Entity character));
            Assert.True(world.IsAlive(character));
            return true;
        },
        out bool accountingRestored));
    Assert.True(accountingRestored);

    using var admissionPort = new LocalAdmissionUiPort(link);
    using var admission = new AdmissionService(fixture.Runtime.Game, fixture.Composition, admissionPort);
    AdmissionResult waiting = admission.JoinAccountBacked(
        new ParticipantId(7008),
        new TrustedLocalIdentity("alice-local"));
    Assert.True(waiting.IsWaitingForCharacter);
    UiListRow restoredAlice = client.GetRows(
        waiting.CharacterSelectView,
        CharacterSelectUi.Characters)[0];
    client.SubmitAction(waiting.CharacterSelectView, CharacterSelectUi.Play, restoredAlice.Id);
    Assert.True(admission.TryGetSession(new ParticipantId(7008), out AdmissionResult entered));
    Assert.True(entered.IsEntered);
    Assert.True(entered.Entity.World != fixture.Character.World);
}

static void FailedInventoryRebuildDoesNotPublishCandidateGeneration()
{
    using Fixture fixture = CreateFixture();
    InventorySaveResult save = fixture.Persistence.TrySave(
        new SaveSetContext(1, 1),
        static capture => capture());
    Assert.True(save.Succeeded);

    EcsWorld activeWorld = fixture.CurrentWorld;
    ulong activeGeneration = fixture.Use((_, generationId, _) => generationId);
    var constrained = new InventoryRuntimePersistence(
        fixture.Runtime.Game,
        fixture.Composition.Catalog,
        InventoryComposition.RestoreOptions(),
        requirements => fixture.Composition.CreateStaging(
            requirements,
            new InventoryLimits(16, 1, 16)),
        new SaveSetCoordinator(fixture.Root));

    InventoryLoadResult result = constrained.TryLoad();

    Assert.False(result.Succeeded);
    Assert.True(result.Failure is InvalidOperationException);
    Assert.True(ReferenceEquals(activeWorld, fixture.CurrentWorld));
    Assert.False(activeWorld.IsDisposed);
    Assert.Equal(activeGeneration, fixture.Use((_, generationId, _) => generationId));
    Assert.Equal(2, fixture.Use((_, _, state) => state.Index.ItemCount));
}

static void EquippedRelationshipPersistsAndReindexes()
{
    using Fixture fixture = CreateFixture();
    InventoryMutationResult equip = fixture.Use((world, _, state) => state.Equip(
        world,
        fixture.Sword,
        fixture.Backpack,
        fixture.Character,
        InventoryEquipSlot.MainHand));
    Assert.True(equip.Accepted);
    InventorySaveResult save = fixture.Persistence.TrySave(
        new SaveSetContext(1, 1),
        static capture => capture());
    Assert.True(save.Succeeded);

    InventoryLoadResult load = fixture.Persistence.TryLoad();

    Assert.True(load.Succeeded);
    fixture.Use((world, _, state) =>
    {
        Assert.True(world.TryResolvePersistentEntity(fixture.CharacterId, out Entity character));
        Assert.True(world.TryResolvePersistentEntity(fixture.BackpackId, out Entity backpack));
        Assert.True(world.TryResolvePersistentEntity(fixture.SwordId, out Entity sword));
        Assert.True(state.TryReadItem(world, sword, out InventoryItem restored));
        Assert.Equal(character, restored.Parent);
        Assert.Equal(InventoryEquipSlot.MainHand, restored.EquipSlot);
        Assert.True(state.Index.TryGetEquipped(character, InventoryEquipSlot.MainHand, out Entity equipped));
        Assert.Equal(sword, equipped);
        Assert.Equal(1, state.Index.GetItems(backpack).Count);
        return true;
    });
}

static void InventoryUiLifetimeIsIdempotentAndOwnerBound()
{
    using Fixture fixture = CreateFixture();
    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    using var port = new InventoryUiPort(link);
    var controller = new InventoryUiController(fixture.Runtime.Game, port);
    Assert.True(controller.Open(fixture.Backpack).Succeeded);

    InventoryIndex index = fixture.Use((_, _, state) => state.Index);
    Exception? foreignControllerFailure = null;
    Exception? foreignIndexFailure = null;
    var thread = new Thread(() =>
    {
        try { _ = controller.OpenViewCount; }
        catch (Exception exception) { foreignControllerFailure = exception; }
        try { _ = index.ItemCount; }
        catch (Exception exception) { foreignIndexFailure = exception; }
    });
    thread.Start();
    thread.Join();
    Assert.True(foreignControllerFailure is InvalidOperationException);
    Assert.True(foreignIndexFailure is InvalidOperationException);

    controller.Dispose();
    controller.Dispose();
    Assert.Equal(0, client.OpenViewCount);
}

static void InventoryUiCloseFailurePreservesOwnership()
{
    using Fixture fixture = CreateFixture();
    var port = new FaultingInventoryUiPort();
    var controller = new InventoryUiController(fixture.Runtime.Game, port);
    InventoryUiOpenResult opened = controller.Open(fixture.Backpack);
    Assert.True(opened.Succeeded);

    port.CloseResult = false;
    Assert.False(controller.Close(opened.View));
    Assert.Equal(1, controller.OpenViewCount);

    port.CloseResult = true;
    Assert.True(controller.Close(opened.View));
    Assert.Equal(0, controller.OpenViewCount);

    Assert.True(controller.Open(fixture.Backpack).Succeeded);
    Assert.True(controller.Open(fixture.Backpack).Succeeded);
    port.ThrowOnClose = true;
    Assert.Throws<InvalidOperationException>(() => controller.Dispose());
    Assert.Equal(4, port.CloseCount);
    controller.Dispose();
}

static void InventoryIndexBaselinesAreCaptured()
{
    using var benchmark = new InventoryBenchmarkFixture(1_000);
    CollectBenchmarkGarbage();
    (double mean10, double p9510, long checksum10, long allocation10) = MeasureEnumeration(benchmark, 10);
    (double mean100, double p95100, long checksum100, long allocation100) = MeasureEnumeration(benchmark, 100);
    (double mean1000, double p951000, long checksum1000, long allocation1000) = MeasureEnumeration(benchmark, 1_000);

    Entity moved = benchmark.Items[^1];
    for (int index = 0; index < 1_000; index++)
    {
        Entity source = (index & 1) == 0 ? benchmark.Source : benchmark.Destination;
        Entity destination = (index & 1) == 0 ? benchmark.Destination : benchmark.Source;
        Assert.True(benchmark.State.Move(benchmark.World, moved, source, destination).Accepted);
    }
    const int moveOperations = 20_000;
    var moveSamples = new double[21];
    long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < moveSamples.Length; sample++)
    {
        long started = Stopwatch.GetTimestamp();
        for (int index = 0; index < moveOperations; index++)
        {
            Entity source = (index & 1) == 0 ? benchmark.Source : benchmark.Destination;
            Entity destination = (index & 1) == 0 ? benchmark.Destination : benchmark.Source;
            if (!benchmark.State.Move(benchmark.World, moved, source, destination).Accepted)
                throw new InvalidOperationException("The inventory move baseline failed.");
        }
        moveSamples[sample] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / moveOperations;
    }
    long moveAllocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;

    const int rejectedOperations = 20_000;
    var rejectedSamples = new double[21];
    allocationBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < rejectedSamples.Length; sample++)
    {
        long started = Stopwatch.GetTimestamp();
        for (int index = 0; index < rejectedOperations; index++)
        {
            InventoryMutationResult rejected = benchmark.State.Move(
                benchmark.World,
                moved,
                benchmark.Destination,
                benchmark.Source);
            if (rejected.Status != InventoryMutationStatus.WrongSource)
                throw new InvalidOperationException("The rejected inventory baseline changed semantics.");
        }
        rejectedSamples[sample] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / rejectedOperations;
    }
    long rejectedAllocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;

    var rebuildSamples = new double[7];
    allocationBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < rebuildSamples.Length; sample++)
    {
        long started = Stopwatch.GetTimestamp();
        benchmark.State.Rebuild(benchmark.World);
        rebuildSamples[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
    long rebuildAllocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;

    const int oneWayWarmup = 16;
    const int oneWayOperations = 64;
    const int oneWaySamplesCount = 21;
    using var oneWay = new InventoryOneWayBenchmarkFixture(
        oneWayWarmup + (oneWayOperations * oneWaySamplesCount));
    for (int index = 0; index < oneWayWarmup; index++)
    {
        Assert.True(oneWay.State.Split(
            oneWay.World,
            oneWay.SplitItems[index],
            oneWay.SplitSource,
            oneWay.SplitDestination,
            1).Accepted);
        Assert.True(oneWay.State.Equip(
            oneWay.World,
            oneWay.EquipItems[index],
            oneWay.EquipContainers[index],
            oneWay.EquipCharacters[index],
            InventoryEquipSlot.MainHand).Accepted);
    }
    CollectBenchmarkGarbage();
    var splitSamples = new double[oneWaySamplesCount];
    allocationBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < splitSamples.Length; sample++)
    {
        int start = oneWayWarmup + (sample * oneWayOperations);
        long started = Stopwatch.GetTimestamp();
        for (int offset = 0; offset < oneWayOperations; offset++)
        {
            if (!oneWay.State.Split(
                    oneWay.World,
                    oneWay.SplitItems[start + offset],
                    oneWay.SplitSource,
                    oneWay.SplitDestination,
                    1).Accepted)
            {
                throw new InvalidOperationException("The inventory split baseline failed.");
            }
        }
        splitSamples[sample] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / oneWayOperations;
    }
    long splitAllocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;

    var equipSamples = new double[oneWaySamplesCount];
    allocationBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < equipSamples.Length; sample++)
    {
        int start = oneWayWarmup + (sample * oneWayOperations);
        long started = Stopwatch.GetTimestamp();
        for (int offset = 0; offset < oneWayOperations; offset++)
        {
            int index = start + offset;
            if (!oneWay.State.Equip(
                    oneWay.World,
                    oneWay.EquipItems[index],
                    oneWay.EquipContainers[index],
                    oneWay.EquipCharacters[index],
                    InventoryEquipSlot.MainHand).Accepted)
            {
                throw new InvalidOperationException("The inventory equip baseline failed.");
            }
        }
        equipSamples[sample] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / oneWayOperations;
    }
    long equipAllocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;

    Assert.True(checksum10 != 0 && checksum100 != 0 && checksum1000 != 0);
    Assert.Equal(0L, allocation10);
    Assert.Equal(0L, allocation100);
    Assert.Equal(0L, allocation1000);
    Assert.Equal(0L, moveAllocation);
    Assert.Equal(0L, rejectedAllocation);
    Assert.Equal(0L, splitAllocation);
    Assert.Equal(0L, equipAllocation);
    Console.WriteLine(
        $"BASELINE S0.7 enumerate10_mean={mean10:F2}ns enumerate10_p95={p9510:F2}ns enumerate10_alloc={allocation10}B " +
        $"enumerate100_mean={mean100:F2}ns enumerate100_p95={p95100:F2}ns enumerate100_alloc={allocation100}B " +
        $"enumerate1000_mean={mean1000:F2}ns enumerate1000_p95={p951000:F2}ns enumerate1000_alloc={allocation1000}B " +
        $"move_mean={Mean(moveSamples):F2}ns move_p95={P95(moveSamples):F2}ns move_alloc={moveAllocation}B " +
        $"reject_mean={Mean(rejectedSamples):F2}ns reject_p95={P95(rejectedSamples):F2}ns reject_alloc={rejectedAllocation}B " +
        $"split_mean={Mean(splitSamples):F2}ns split_p95={P95(splitSamples):F2}ns split_alloc={splitAllocation}B " +
        $"equip_mean={Mean(equipSamples):F2}ns equip_p95={P95(equipSamples):F2}ns equip_alloc={equipAllocation}B " +
        $"rebuild_mean={Mean(rebuildSamples):F3}ms rebuild_p95={P95(rebuildSamples):F3}ms " +
        $"rebuild_alloc_per_op={rebuildAllocation / rebuildSamples.Length}B " +
        $"reserved_container_slots={benchmark.State.Index.CaptureSnapshot().ReservedContainerItemSlots}");
}

static (double Mean, double P95, long Checksum, long Allocation) MeasureEnumeration(
    InventoryBenchmarkFixture benchmark,
    int rows)
{
    const int operations = 20_000;
    var samples = new double[21];
    long checksum = 0;
    IReadOnlyList<Entity> items = benchmark.State.Index.GetItems(benchmark.Source);
    for (int operation = 0; operation < 1_000; operation++)
    {
        for (int row = 0; row < rows; row++)
            checksum = unchecked(checksum + items[row].Index);
    }
    long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < samples.Length; sample++)
    {
        long started = Stopwatch.GetTimestamp();
        for (int operation = 0; operation < operations; operation++)
        {
            for (int row = 0; row < rows; row++)
                checksum = unchecked(checksum + items[row].Index);
        }
        samples[sample] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / operations;
    }
    long allocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
    return (Mean(samples), P95(samples), checksum, allocation);
}

static double Mean(double[] samples) => samples.Average();

static void CollectBenchmarkGarbage()
{
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
}

static double P95(double[] samples)
{
    double[] ordered = samples.ToArray();
    Array.Sort(ordered);
    return ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
}

static UiRowId FindRow(ReadOnlySpan<UiListRow> rows, string itemName)
{
    foreach (UiListRow row in rows)
    {
        if (row.Values.Length > 0 && row.Values[0].Text == itemName)
            return row.Id;
    }
    throw new InvalidOperationException($"The inventory row '{itemName}' was not found.");
}

static Fixture CreateFixture()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-s0-7-" + Guid.NewGuid().ToString("N"));
    var composition = new InventoryComposition();
    var runtime = new ServerRuntime(
        composition,
        new ServerRuntimeOptions(InventoryComposition.WorldOptions(), WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();

    Assert.True(runtime.Game.TryUseActiveGeneration<InventoryGameGenerationState, FixtureEntities>(
        (world, _, game) =>
        {
            InventoryGenerationState state = game.Inventory;
            Assert.True(state.TryCreateCharacter(world, out Entity character));
            Assert.True(state.TryCreateContainer(world, character, 8, out Entity backpack));
            Assert.True(state.TryCreateItem(world, backpack, InventoryItemKind.Bread, 5, out Entity bread));
            Assert.True(state.TryCreateItem(world, backpack, InventoryItemKind.IronSword, 1, out Entity sword));
            Assert.True(world.TryGetPersistentId(character, out PersistentEntityId characterId));
            Assert.True(world.TryGetPersistentId(backpack, out PersistentEntityId backpackId));
            Assert.True(world.TryGetPersistentId(bread, out PersistentEntityId breadId));
            Assert.True(world.TryGetPersistentId(sword, out PersistentEntityId swordId));
            Assert.True(world.TryGetPrefabProvenance(character, out PrefabKey characterPrefab));
            Assert.Equal(InventoryPrefabKeys.Character, characterPrefab);
            var accountId = new AccountId(1);
            game.Accounting.AddAccount(new AccountRecord(accountId));
            game.Accounting.AddCharacter(new CharacterRecord(
                new CharacterId(1),
                accountId,
                0,
                "Alice",
                characterPrefab,
                new PersistentEntityKey(world.PersistentWorldId, characterId)));
            game.Accounting.BindTrustedIdentity(new TrustedLocalIdentity("alice-local"), accountId);
            return new FixtureEntities(
                character,
                backpack,
                bread,
                sword,
                characterId,
                backpackId,
                breadId,
                swordId);
        },
        out FixtureEntities entities));

    var coordinator = new SaveSetCoordinator(root);
    var persistence = new InventoryRuntimePersistence(
        runtime.Game,
        composition.Catalog,
        InventoryComposition.RestoreOptions(),
        requirements => composition.CreateStaging(requirements),
        coordinator);
    return new Fixture(root, runtime, composition, persistence, entities);
}

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
    internal Fixture(
        string root,
        ServerRuntime runtime,
        InventoryComposition composition,
        InventoryRuntimePersistence persistence,
        FixtureEntities entities)
    {
        Root = root;
        Runtime = runtime;
        Composition = composition;
        Persistence = persistence;
        Character = entities.Character;
        Backpack = entities.Backpack;
        Bread = entities.Bread;
        Sword = entities.Sword;
        CharacterId = entities.CharacterId;
        BackpackId = entities.BackpackId;
        BreadId = entities.BreadId;
        SwordId = entities.SwordId;
    }

    internal string Root { get; }
    internal ServerRuntime Runtime { get; }
    internal InventoryComposition Composition { get; }
    internal InventoryRuntimePersistence Persistence { get; }
    internal Entity Character { get; }
    internal Entity Backpack { get; }
    internal Entity Bread { get; }
    internal Entity Sword { get; }
    internal PersistentEntityId CharacterId { get; }
    internal PersistentEntityId BackpackId { get; }
    internal PersistentEntityId BreadId { get; }
    internal PersistentEntityId SwordId { get; }

    internal EcsWorld CurrentWorld
    {
        get
        {
            Assert.True(Runtime.Game.TryUseActiveGeneration(static (world, _) => world, out EcsWorld world));
            return world;
        }
    }

    internal TResult Use<TResult>(ServerGenerationStateOperation<InventoryGenerationState, TResult> operation)
    {
        Assert.True(Runtime.Game.TryUseActiveGeneration<IInventoryGenerationStateProvider, TResult>(
            (world, generationId, provider) => operation(world, generationId, provider.Inventory),
            out TResult result));
        return result;
    }

    internal InventoryItem ReadItem(Entity item) => Use((world, _, state) =>
    {
        Assert.True(state.TryReadItem(world, item, out InventoryItem value));
        return value;
    });

    public void Dispose()
    {
        Runtime.Dispose();
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

sealed class InventoryComposition : IServerComposition, IAdmissionEntityBinder
{
    private readonly Dictionary<WorldId, InventoryPrefabHandles> _prefabs = [];
    internal InventoryComposition()
    {
        Schema = InventorySchema.CreateSchema();
        Catalog = InventorySchema.CreateCatalog(Schema);
    }

    public ComponentSchema Schema { get; }
    internal PersistenceCatalog Catalog { get; }
    internal EcsWorld? LastWorld { get; private set; }

    public void Configure(ServerGenerationBuilder generation)
    {
        InventoryPrefabHandles prefabs = InventoryContent.CompileDefaults(
            generation.World,
            new ContentCompilationOptions(ChunkLayoutOptions.KiB16, maximumChunksPerNewArchetype: 8));
        _prefabs.Add(generation.World.Id, prefabs);
        LastWorld = generation.World;
        generation.SetGameState(new InventoryGameGenerationState(
            new AccountingStore(),
            new InventoryGenerationState(
                generation.World,
                prefabs,
                new InventoryLimits(16, 64, 16))));
    }

    internal InventoryStaging CreateStaging(PersistenceRestoreRequirements requirements) =>
        CreateStaging(requirements, new InventoryLimits(16, 64, 16));

    internal InventoryStaging CreateStaging(
        PersistenceRestoreRequirements requirements,
        InventoryLimits limits)
    {
        var world = new EcsWorld(requirements.Schema, new EcsWorldOptions(
            new EntityCapacity(Math.Max(64, requirements.EntityCount)),
            maximumArchetypes: 8,
            maximumPreparedPlans: 8,
            scheduler: new EcsSchedulerOptions(
                maximumSystems: 1,
                maximumDependencies: 1,
                maximumWorkItems: 2,
                maximumStructuralSegments: 2,
                backgroundWorkerCount: 0),
            persistence: requirements.IdentityOptions));
        InventoryPrefabHandles prefabs = InventoryContent.CompileDefaults(
            world,
            new ContentCompilationOptions(ChunkLayoutOptions.KiB16, maximumChunksPerNewArchetype: 8));
        _prefabs.Add(world.Id, prefabs);
        return new InventoryStaging(
            world,
            new InventoryGenerationState(
                world,
                prefabs,
                limits));
    }

    internal static EcsWorldOptions WorldOptions() => new(
        new EntityCapacity(64),
        maximumArchetypes: 8,
        maximumPreparedPlans: 8,
        scheduler: new EcsSchedulerOptions(
            maximumSystems: 1,
            maximumDependencies: 1,
            maximumWorkItems: 2,
            maximumStructuralSegments: 2,
            backgroundWorkerCount: 0),
        persistence: new PersistenceIdentityOptions(new PersistentWorldId(0x7007)));

    internal static PersistenceRestoreOptions RestoreOptions() => new(
        new PersistenceLimits(1_000_000, 64, 16, 16, 128, 1_000_000, 1_000_000),
        ChunkLayoutOptions.KiB16);

    public bool TryCreatePlayable(EcsWorld world, ParticipantId participant, out Entity entity)
    {
        entity = Entity.Null;
        return _prefabs.TryGetValue(world.Id, out InventoryPrefabHandles prefabs) &&
            world.TryInstantiate(prefabs.Character, out entity);
    }

    public bool IsPlayable(EcsWorld world, in CharacterRecord character, Entity resolvedEntity) =>
        world.IsAlive(resolvedEntity) &&
        world.TryGetPrefabProvenance(resolvedEntity, out PrefabKey prefab) &&
        prefab == character.PrefabKey;

}

sealed class InventoryUiPort : IInventoryUiPort, IDisposable
{
    private readonly LocalSemanticLink _link;

    internal InventoryUiPort(LocalSemanticLink link) => _link = link;

    internal int HotOpenCount { get; private set; }

    public event Action<UiActionRequest>? ActionSubmitted
    {
        add => _link.ActionSubmitted += value;
        remove => _link.ActionSubmitted -= value;
    }

    public UiViewInstanceId OpenView(UiOpenView operation) => _link.OpenView(operation);
    public UiViewInstanceId OpenHotView(UiHotOpenView operation)
    {
        HotOpenCount++;
        return _link.OpenHotView(operation);
    }
    public void UpdateView(UiUpdateView operation) => _link.UpdateView(operation);
    public bool CloseView(UiCloseView operation) => _link.CloseView(operation);
    public void Dispose() { }
}

sealed class FaultingInventoryUiPort : IInventoryUiPort
{
    private ulong _nextInstance = 1;

    public event Action<UiActionRequest>? ActionSubmitted;

    public bool CloseResult { get; set; } = true;
    public bool ThrowOnClose { get; set; }
    public int CloseCount { get; private set; }

    public UiViewInstanceId OpenView(UiOpenView operation) => new(_nextInstance++);

    public UiViewInstanceId OpenHotView(UiHotOpenView operation) => new(_nextInstance++);

    public void UpdateView(UiUpdateView operation) { }

    public bool CloseView(UiCloseView operation)
    {
        CloseCount++;
        if (ThrowOnClose)
            throw new InvalidOperationException("Injected inventory close failure.");
        return CloseResult;
    }

    public void Submit(UiActionRequest request) => ActionSubmitted?.Invoke(request);
}

sealed class InventoryBenchmarkFixture : IDisposable
{
    internal InventoryBenchmarkFixture(int population)
    {
        ComponentSchema schema = InventorySchema.CreateSchema();
        World = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(population + 16),
            maximumArchetypes: 8,
            maximumPreparedPlans: 8,
            scheduler: new EcsSchedulerOptions(
                maximumSystems: 1,
                maximumDependencies: 1,
                maximumWorkItems: 2,
                maximumStructuralSegments: 2,
                backgroundWorkerCount: 0),
            persistence: new PersistenceIdentityOptions(new PersistentWorldId(0x7017))));
        InventoryPrefabHandles prefabs = InventoryContent.CompileDefaults(
            World,
            new ContentCompilationOptions(ChunkLayoutOptions.KiB16, maximumChunksPerNewArchetype: 16));
        State = new InventoryGenerationState(
            World,
            prefabs,
            new InventoryLimits(4, checked(population * 2), population));
        ItemLookup = World.PrepareLookup<InventoryItem>();
        World.Seal();
        World.Initialize();
        World.BeginRun();
        if (!State.TryCreateCharacter(World, out Entity character) ||
            !State.TryCreateContainer(World, character, population, out Entity source) ||
            !State.TryCreateContainer(World, character, population, out Entity destination))
        {
            throw new InvalidOperationException("The inventory benchmark fixture could not create its roots.");
        }
        Source = source;
        Destination = destination;
        Items = new Entity[population];
        for (int index = 0; index < Items.Length; index++)
        {
            if (!State.TryCreateItem(World, Source, InventoryItemKind.Bread, 1, out Items[index]))
                throw new InvalidOperationException("The inventory benchmark fixture could not create its population.");
        }
    }

    internal EcsWorld World { get; }
    internal InventoryGenerationState State { get; }
    internal ComponentLookup<InventoryItem> ItemLookup { get; }
    internal Entity Source { get; }
    internal Entity Destination { get; }
    internal Entity[] Items { get; }

    public void Dispose()
    {
        State.Dispose();
        World.Dispose();
    }
}

sealed class InventoryOneWayBenchmarkFixture : IDisposable
{
    internal InventoryOneWayBenchmarkFixture(int operationCount)
    {
        int entityCapacity = checked((operationCount * 5) + 8);
        ComponentSchema schema = InventorySchema.CreateSchema();
        World = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(entityCapacity),
            maximumArchetypes: 8,
            maximumPreparedPlans: 8,
            scheduler: new EcsSchedulerOptions(
                maximumSystems: 1,
                maximumDependencies: 1,
                maximumWorkItems: 2,
                maximumStructuralSegments: 2,
                backgroundWorkerCount: 0),
            persistence: new PersistenceIdentityOptions(new PersistentWorldId(0x7027))));
        InventoryPrefabHandles prefabs = InventoryContent.CompileDefaults(
            World,
            new ContentCompilationOptions(ChunkLayoutOptions.KiB16, maximumChunksPerNewArchetype: 64));
        State = new InventoryGenerationState(
            World,
            prefabs,
            new InventoryLimits(operationCount + 2, operationCount * 3, operationCount));
        World.Seal();
        World.Initialize();
        World.BeginRun();

        if (!State.TryCreateCharacter(World, out Entity splitOwner) ||
            !State.TryCreateContainer(World, splitOwner, operationCount, out Entity splitSource) ||
            !State.TryCreateContainer(World, splitOwner, operationCount, out Entity splitDestination))
        {
            throw new InvalidOperationException("The one-way inventory benchmark could not create split roots.");
        }
        SplitSource = splitSource;
        SplitDestination = splitDestination;
        SplitItems = new Entity[operationCount];
        EquipCharacters = new Entity[operationCount];
        EquipContainers = new Entity[operationCount];
        EquipItems = new Entity[operationCount];
        for (int index = 0; index < operationCount; index++)
        {
            if (!State.TryCreateItem(World, SplitSource, InventoryItemKind.Bread, 2, out SplitItems[index]) ||
                !State.TryCreateCharacter(World, out EquipCharacters[index]) ||
                !State.TryCreateContainer(World, EquipCharacters[index], 1, out EquipContainers[index]) ||
                !State.TryCreateItem(World, EquipContainers[index], InventoryItemKind.IronSword, 1, out EquipItems[index]))
            {
                throw new InvalidOperationException("The one-way inventory benchmark could not create its operations.");
            }
        }
    }

    internal EcsWorld World { get; }
    internal InventoryGenerationState State { get; }
    internal Entity SplitSource { get; }
    internal Entity SplitDestination { get; }
    internal Entity[] SplitItems { get; }
    internal Entity[] EquipCharacters { get; }
    internal Entity[] EquipContainers { get; }
    internal Entity[] EquipItems { get; }

    public void Dispose()
    {
        State.Dispose();
        World.Dispose();
    }
}

sealed class MemoryRenderer : IUiRenderer
{
    public int CreateCount { get; private set; }
    public int ApplyCount { get; private set; }
    public int CloseCount { get; private set; }

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        CreateCount++;
        return new Handle();
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction) => ApplyCount++;

    public void Close(IUiRenderHandle handle) => CloseCount++;

    private sealed class Handle : IUiRenderHandle;
}

static class Assert
{
    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    public static void True(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected true.");
    }

    public static void False(bool value)
    {
        if (value)
            throw new InvalidOperationException("Expected false.");
    }
}
