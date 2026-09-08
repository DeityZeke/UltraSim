using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication.Tests;

internal static class M00SharedPlayableAuthorityTests
{
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

    private static void CheckHorizontalEqual(WorldPosition expected, WorldPosition actual)
    {
        Check.Equal(expected.GlobalXMillimeters, actual.GlobalXMillimeters);
        Check.Equal(expected.GlobalZMillimeters, actual.GlobalZMillimeters);
    }

    private static Entity Resolve(EcsWorld world, PersistentEntityId id) =>
        world.TryResolvePersistentEntity(id, out Entity entity)
            ? entity
            : throw new InvalidOperationException($"Persistent entity {id} was unavailable.");
}
