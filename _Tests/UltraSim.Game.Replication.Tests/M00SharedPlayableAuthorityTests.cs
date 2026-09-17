using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Inventory;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication.Tests;

internal static class M00SharedPlayableAuthorityTests
{
    internal static void SharedActorPresentationPreparationIsVersionedAndSlotIndexed()
    {
        using SharedPlayableAuthority authority = SharedPlayableAuthority.CreateConceptDemo(
            new SharedPlayableAuthorityLimits(3));
        Check.True(authority.Join(
            new ParticipantId(301),
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Alice"),
            out SharedPlayableParticipant alice).IsEntered);
        Check.True(authority.Join(
            new ParticipantId(302),
            new GameGuestAdmissionRequest(GameGuestBody.HumanMale, "Bob"),
            out SharedPlayableParticipant bob).IsEntered);
        Check.True(authority.Join(
            new ParticipantId(303),
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Carol"),
            out SharedPlayableParticipant carol).IsEntered);

        SharedActorPresentationCensus before = authority.GetActorPresentationCensus();
        SharedActorPresentationView first = authority.GetActorPresentationView();
        Check.Equal(3, first.Count);
        Check.Equal(3, first.Capacity);
        Check.True(first.Version > 0);
        Check.True(first.TryGet(alice.Slot, out GamePublicActorPresentation aliceView));
        Check.Equal(alice, aliceView.Participant);
        Check.True(first.TryGet(bob.Slot, out GamePublicActorPresentation bobView));
        Check.Equal(bob, bobView.Participant);
        Check.True(first.TryGet(carol.Slot, out GamePublicActorPresentation carolView));
        Check.Equal(carol, carolView.Participant);

        for (int read = 0; read < 128; read++)
        {
            SharedActorPresentationView repeated = authority.GetActorPresentationView();
            Check.Equal(first.Version, repeated.Version);
        }
        SharedActorPresentationCensus repeatedCensus = authority.GetActorPresentationCensus();
        Check.Equal(before.Preparations + 1, repeatedCensus.Preparations);
        Check.Equal(before.ParticipantRecordsExamined + 3,
            repeatedCensus.ParticipantRecordsExamined);
        Check.Equal(before.ViewReads + 129, repeatedCensus.ViewReads);

        authority.Runtime.Tick();
        SharedActorPresentationView afterTick = authority.GetActorPresentationView();
        SharedActorPresentationCensus tickCensus = authority.GetActorPresentationCensus();
        Check.Equal(repeatedCensus.Preparations + 1, tickCensus.Preparations);
        Check.Equal(repeatedCensus.ParticipantRecordsExamined + 3,
            tickCensus.ParticipantRecordsExamined);
        Check.Equal(first.Version, afterTick.Version);

        Check.True(authority.RecordPresentationAction(
            in alice, 1, GamePublicActorActionKind.Interact));
        SharedActorPresentationView afterAction = authority.GetActorPresentationView();
        Check.Equal(checked(first.Version + 1), afterAction.Version);
        Check.True(afterAction.TryGet(alice.Slot, out aliceView));
        Check.Equal(GamePublicActorActionKind.Interact, aliceView.Action);
        Check.Equal(1UL, aliceView.ActionSequence);

        Check.True(authority.Leave(bob));
        SharedActorPresentationView afterLeave = authority.GetActorPresentationView();
        Check.Equal(2, afterLeave.Count);
        Check.False(afterLeave.TryGet(bob.Slot, out _));
        ulong leaveVersion = afterLeave.Version;
        Check.True(authority.Join(
            new ParticipantId(304),
            new GameGuestAdmissionRequest(GameGuestBody.HumanMale, "Bob Again"),
            out SharedPlayableParticipant replacement).IsEntered);
        Check.Equal(bob.Slot, replacement.Slot);
        SharedActorPresentationView afterReuse = authority.GetActorPresentationView();
        Check.True(afterReuse.Version > leaveVersion);
        Check.True(afterReuse.TryGet(replacement.Slot, out GamePublicActorPresentation replacementView));
        Check.Equal(replacement, replacementView.Participant);
        Check.False(replacementView.Participant.PublicEntity == bob.PublicEntity);

        Check.True(authority.RecordPresentationAction(
            in alice, 2, GamePublicActorActionKind.Primary));
        _ = authority.GetActorPresentationView();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (ulong sequence = 3; sequence < 131; sequence++)
        {
            Check.True(authority.RecordPresentationAction(
                in alice,
                sequence,
                (sequence & 1) == 0
                    ? GamePublicActorActionKind.Primary
                    : GamePublicActorActionKind.Interact));
            SharedActorPresentationView warmed = authority.GetActorPresentationView();
            Check.True(warmed.TryGet(alice.Slot, out _));
        }
        Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);

        Check.True(authority.Leave(replacement));
        Check.True(authority.Leave(carol));
        Check.True(authority.Leave(alice));
    }

    internal static void ConfiguredParticipantCapacityReservesParticipantEntities()
    {
        const int participantCapacity = 50;
        var limits = new SharedPlayableAuthorityLimits(participantCapacity);
        Check.Equal(406, limits.RequiredEntityCapacity);
        Check.Equal(66, limits.RequiredInventoryLimits.MaximumContainers);
        Check.Equal(128, limits.RequiredInventoryLimits.MaximumItems);
        using SharedPlayableAuthority authority = SharedPlayableAuthority.CreateConceptDemo(limits);
        var participants = new SharedPlayableParticipant[participantCapacity];
        for (int index = 0; index < participants.Length; index++)
        {
            AdmissionResult entered = authority.Join(
                new ParticipantId(checked((ulong)10_000 + (ulong)index)),
                new GameGuestAdmissionRequest(
                    (index & 1) == 0 ? GameGuestBody.HumanFemale : GameGuestBody.HumanMale,
                    $"Capacity {index:D2}"),
                out participants[index]);
            Check.True(entered.IsEntered);
            Check.True(participants[index].IsValid);
        }

        SharedPlayableAuthoritySnapshot full = authority.GetSnapshot();
        Check.Equal(participantCapacity, full.ParticipantCount);
        Check.Equal(participantCapacity, full.MaximumParticipants);
        var presentations = new GamePublicActorPresentation[participantCapacity];
        Check.Equal(participantCapacity, authority.CopyActorPresentations(presentations));

        AdmissionResult excess = authority.Join(
            new ParticipantId(20_000),
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Capacity Excess"),
            out SharedPlayableParticipant rejected);
        Check.False(excess.IsEntered);
        Check.False(rejected.IsValid);

        for (int index = participants.Length - 1; index >= 0; index--)
            Check.True(authority.Leave(participants[index]));
        SharedPlayableAuthoritySnapshot retired = authority.GetSnapshot();
        Check.Equal(0, retired.ParticipantCount);
        Check.Equal((ulong)participantCapacity, retired.Joins);
        Check.Equal((ulong)participantCapacity, retired.Leaves);
    }

    internal static void SpawnedWeaponRetainsIdentityAcrossWorldInventoryEquipAndAnotherOwner()
    {
        using SharedPlayableAuthority authority = SharedPlayableAuthority.CreateConceptDemo(
            new SharedPlayableAuthorityLimits(2));
        Check.True(authority.Join(
            new ParticipantId(201),
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Alice"),
            out SharedPlayableParticipant alice).IsEntered);
        Check.True(authority.Join(
            new ParticipantId(202),
            new GameGuestAdmissionRequest(GameGuestBody.HumanMale, "Bob"),
            out SharedPlayableParticipant bob).IsEntered);
        Check.True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, long>(
            static (_, _, game) => game.Items.ReservedBytes,
            out long itemReservedBytes));

        SpawnCapture first = ReadActiveSpawn(authority);
        Check.Equal(0U, first.Weapon.SpawnOrdinal);
        Check.True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                Entity aliceActor = Resolve(world, alice.Entities.PlayerId);
                Entity aliceBackpack = Resolve(world, alice.Entities.BackpackId);
                Check.Equal(GameWorldActionStatus.Accepted, game.Items.TryPickup(
                    world, game.WorldState, game.Inventory, aliceActor, first.Item, aliceBackpack));
                Check.True(world.TryGetPersistentId(first.Item, out PersistentEntityId retained));
                Check.Equal(first.PersistentId, retained);
                Check.True(game.Items.TryReadWeapon(world, first.Item, out GameWeaponInstance retainedWeapon));
                Check.Equal(first.Weapon, retainedWeapon);
                Check.True(game.Inventory.TryReadItem(world, first.Item, out InventoryItem contained));
                Check.Equal(aliceBackpack, contained.Parent);
                Check.True(game.WorldState.TryReadPublic(
                    world, first.Item, out WorldPublicDescriptor hidden));
                Check.Equal(WorldPublicStateFlags.None, hidden.Flags);
                Check.True(game.Inventory.Equip(
                    world,
                    first.Item,
                    aliceBackpack,
                    aliceActor,
                    InventoryEquipSlot.MainHand).Accepted);
                Check.Equal(
                    GameWorldActionStatus.Accepted,
                    game.Items.TryUsePrimary(world, game.Inventory, aliceActor));
                return true;
            }, out bool pickedUp) && pickedUp);

        for (int tick = 0; tick < GameItemGenerationState.RespawnDelayTicks; tick++)
            authority.Runtime.Tick();

        SpawnCapture second = ReadActiveSpawn(authority);
        Check.Equal(1U, second.Weapon.SpawnOrdinal);
        Check.False(first.PersistentId == second.PersistentId);
        Check.False(first.Weapon.Equals(second.Weapon));
        Check.True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                Entity aliceActor = Resolve(world, alice.Entities.PlayerId);
                Entity bobActor = Resolve(world, bob.Entities.PlayerId);
                Entity bobBackpack = Resolve(world, bob.Entities.BackpackId);
                Check.True(game.WorldState.TryReadPublic(
                    world, first.Item, out WorldPublicDescriptor equipped));
                Check.True((equipped.Flags & WorldPublicStateFlags.Visible) != 0);
                Check.True((equipped.Flags & WorldPublicStateFlags.GroundItem) == 0);
                Check.Equal(GameWorldActionStatus.MissingTarget, game.Items.TryPickup(
                    world, game.WorldState, game.Inventory, bobActor, first.Item, bobBackpack));
                Check.Equal(
                    GameWorldActionStatus.Accepted,
                    game.Items.TryDrop(world, game.WorldState, game.Inventory, aliceActor, first.Item));
                Check.True(game.WorldState.TryReadPublic(
                    world, first.Item, out WorldPublicDescriptor droppedPublic));
                Check.Equal(
                    WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem,
                    droppedPublic.Flags);
                Check.True(world.TryGetPersistentId(first.Item, out PersistentEntityId dropped));
                Check.Equal(first.PersistentId, dropped);
                Check.True(game.Items.TryReadWeapon(world, first.Item, out GameWeaponInstance droppedWeapon));
                Check.Equal(first.Weapon, droppedWeapon);
                Check.Equal(GameWorldActionStatus.Accepted, game.Items.TryPickup(
                    world, game.WorldState, game.Inventory, bobActor, first.Item, bobBackpack));
                Check.True(game.Inventory.TryReadItem(world, first.Item, out InventoryItem transferred));
                Check.Equal(bobBackpack, transferred.Parent);
                Check.True(world.TryGetPersistentId(first.Item, out PersistentEntityId transferredId));
                Check.Equal(first.PersistentId, transferredId);
                return true;
            }, out bool transferred) && transferred);

        Console.WriteLine(
            $"EVIDENCE I0.12 shared_item first={first.PersistentId.Value} " +
            $"roll0={Describe(first.Weapon)} second={second.PersistentId.Value} " +
            $"roll1={Describe(second.Weapon)} respawn_ticks={GameItemGenerationState.RespawnDelayTicks} " +
            $"reserved_bytes={itemReservedBytes} " +
            "same_entity_world_inventory_equip_drop_other_owner=pass");
        Check.True(authority.Leave(bob));
        Check.True(authority.Leave(alice));
    }

    internal static void OneGenerationOwnsIndependentParticipantsAndSurvivesLeave()
    {
        using SharedPlayableAuthority authority = SharedPlayableAuthority.CreateConceptDemo(
            new SharedPlayableAuthorityLimits(3));
        ulong generation = authority.GetSnapshot().GenerationId;
        AdmissionResult aliceAdmission = authority.Join(
            new ParticipantId(101),
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Alice"),
            out SharedPlayableParticipant alice);
        AdmissionResult bobAdmission = authority.Join(
            new ParticipantId(102),
            new GameGuestAdmissionRequest(GameGuestBody.HumanMale, "Bob"),
            out SharedPlayableParticipant bob);
        AdmissionResult carolAdmission = authority.Join(
            new ParticipantId(103),
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Carol"),
            out SharedPlayableParticipant carol);

        Check.True(aliceAdmission.IsEntered && bobAdmission.IsEntered && carolAdmission.IsEntered);
        Check.True(alice.IsValid && bob.IsValid && carol.IsValid);
        Check.Equal(3, authority.GetSnapshot().ParticipantCount);
        Check.Equal(generation, authority.GetSnapshot().GenerationId);
        Check.False(alice.Entities.PlayerId == bob.Entities.PlayerId);
        Check.False(bob.Entities.PlayerId == carol.Entities.PlayerId);
        Check.False(alice.PublicEntity == bob.PublicEntity);
        AdmissionResult excess = authority.Join(
            new ParticipantId(105),
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Excess"),
            out SharedPlayableParticipant rejected);
        Check.False(excess.IsEntered);
        Check.False(rejected.IsValid);
        Check.Equal(AdmissionRejection.EntityCreationFailed, excess.Rejection);
        Check.Equal(3, authority.GetSnapshot().ParticipantCount);
        authority.Runtime.Tick();
        Check.True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, int>(
            (_, _, game) =>
            {
                int characters = 0;
                SpatialChunkCoord start = GamePlayableWorld.PlayerStart.Chunk;
                for (int x = start.X; x <= start.X + 1; x++)
                {
                    if (!game.WorldState.Publications.TryGetCommittedView(
                            new SpatialChunkKey(GameWorldState.DefaultSpace, x, start.Z),
                            out ChunkPublicationView view))
                        continue;
                    foreach (ChunkPublicationPageView page in view)
                        foreach (uint prefab in page.PrefabIds)
                            if (prefab is GameWorldPublicPrefabIds.Character or
                                GameWorldPublicPrefabIds.CharacterMale)
                                characters++;
                }
                return characters;
            }, out int authoritativeCharacters));
        Check.Equal(3, authoritativeCharacters);

        WorldPosition bobBefore = ReadPosition(authority, bob.Entities.PlayerId);
        WorldPosition carolBefore = ReadPosition(authority, carol.Entities.PlayerId);
        var command = new GamePlayerCommand(
            alice.ControlSession,
            1,
            GameLocomotionUpdate.CreateDirect(1_000, 0),
            GamePlayerActions.None,
            GamePlayerActions.None);
        GamePlayerCommandResult result = default;
        Check.True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                Entity actor = Resolve(world, alice.Entities.PlayerId);
                game.WorldActions.ApplyPlayerCommand(
                    world, game.WorldState, actor, command, Entity.Null, out result);
                return true;
            }, out bool applied) && applied);
        authority.Runtime.Tick();
        Check.True(result.WasAdmitted);
        CheckHorizontalEqual(bobBefore, ReadPosition(authority, bob.Entities.PlayerId));
        CheckHorizontalEqual(carolBefore, ReadPosition(authority, carol.Entities.PlayerId));

        Check.True(authority.Leave(bob));
        SharedPlayableAuthoritySnapshot afterLeave = authority.GetSnapshot();
        Check.Equal(2, afterLeave.ParticipantCount);
        Check.Equal(generation, afterLeave.GenerationId);
        CheckHorizontalEqual(carolBefore, ReadPosition(authority, carol.Entities.PlayerId));

        AdmissionResult rejoined = authority.Join(
            new ParticipantId(104),
            new GameGuestAdmissionRequest(GameGuestBody.HumanMale, "Bob Again"),
            out SharedPlayableParticipant replacement);
        Check.True(rejoined.IsEntered && replacement.IsValid);
        Check.Equal(bob.Slot, replacement.Slot);
        Check.False(bob.PublicEntity == replacement.PublicEntity);
        Check.Equal(generation, authority.GetSnapshot().GenerationId);

        Check.True(authority.Leave(replacement));
        Check.True(authority.Leave(carol));
        Check.True(authority.Leave(alice));
        SharedPlayableAuthoritySnapshot final = authority.GetSnapshot();
        Check.Equal(0, final.ParticipantCount);
        Check.Equal(4UL, final.Joins);
        Check.Equal(4UL, final.Leaves);
        Console.WriteLine(
            $"EVIDENCE M0.0 shared_authority generation={generation} joins={final.Joins} " +
            $"leaves={final.Leaves} max={final.MaximumParticipants}");
    }

    private static WorldPosition ReadPosition(
        SharedPlayableAuthority authority,
        PersistentEntityId id)
    {
        Check.True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
            WorldPosition>((world, _, game) =>
            {
                Entity actor = Resolve(world, id);
                if (!game.WorldState.TryReadPosition(world, actor, out WorldPosition position))
                    throw new InvalidOperationException("The shared actor position was unavailable.");
                return position;
            }, out WorldPosition result));
        return result;
    }

    private static SpawnCapture ReadActiveSpawn(SharedPlayableAuthority authority)
    {
        Check.True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
            SpawnCapture>((world, _, game) =>
            {
                Entity spawner = Resolve(world, authority.Scene.IronSwordSpawnerId);
                Check.True(game.Items.TryReadSpawner(world, spawner, out GameItemSpawnerState state));
                Check.True(state.ActiveItem.IsValid);
                Check.True(game.Items.TryReadWeapon(
                    world, state.ActiveItem, out GameWeaponInstance weapon));
                Check.True(world.TryGetPersistentId(
                    state.ActiveItem, out PersistentEntityId persistentId));
                return new SpawnCapture(state.ActiveItem, persistentId, weapon);
            }, out SpawnCapture result));
        return result;
    }

    private static string Describe(in GameWeaponInstance weapon) =>
        $"{weapon.DamageMinimum}-{weapon.DamageMaximum}/" +
        $"{weapon.CurrentDurability}/{weapon.MaximumDurability}/" +
        $"q{weapon.Quality}/{weapon.Element}/o{weapon.SpawnOrdinal}";

    private static void CheckHorizontalEqual(WorldPosition expected, WorldPosition actual)
    {
        Check.Equal(expected.GlobalXMillimeters, actual.GlobalXMillimeters);
        Check.Equal(expected.GlobalZMillimeters, actual.GlobalZMillimeters);
    }

    private static Entity Resolve(EcsWorld world, PersistentEntityId id) =>
        world.TryResolvePersistentEntity(id, out Entity entity)
            ? entity
            : throw new InvalidOperationException($"Persistent entity {id} was unavailable.");

    private readonly record struct SpawnCapture(
        Entity Item,
        PersistentEntityId PersistentId,
        GameWeaponInstance Weapon);
}
