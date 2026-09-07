using UltraSim.Client.Core;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Spatial;

internal static class ClientActorPresentationTests
{
    internal static void Run()
    {
        BoundWorldPack world = ClientWorldSemanticCacheTests.CreateWorld();
        var cache = new ClientWorldSemanticCache(
            world, new ClientWorldSemanticCacheLimits(1, 4, 1, 2, 2));
        SpatialChunkKey key = ClientWorldSemanticCacheTests.Key(0, 0);
        PublicationEpoch epoch = PublicationEpoch.Create();
        WorldPublicEntityId aliceId = new(7, 2);
        WorldPublicEntityId bobId = new(9, 5);
        WorldPublicRecord[] publicRecords =
        [
            Public(aliceId, GameWorldPublicPrefabIds.Character, key, 1_000, 1),
            Public(bobId, GameWorldPublicPrefabIds.CharacterMale, key, 2_000, 1),
        ];
        True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
        var cursor = new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(1));
        True(cache.TryStageFromBaseWorld(handle, publicRecords, cursor, out _));
        True(cache.TryPublish(handle, out _));

        var metadata = new ClientPublicPresentationMetadataState(2);
        metadata.ResetServerGeneration(81);
        Apply(metadata, aliceId, 1, "Alice");
        Apply(metadata, bobId, 1, "Bob");

        var actors = new ClientActorPresentationState(
            ClientActorDefinitionCatalog.ConceptDemo, 2);
        ClientSemanticChunkHandle[] presented = [handle];
        var changes = new ClientActorPresentationChange[4];

        Equal(ClientActorPresentationSyncStatus.DestinationTooSmall,
            actors.TrySynchronize(cache, presented, metadata, 81, changes.AsSpan(0, 1),
                out int rejectedCount, out int required));
        Equal(0, rejectedCount);
        Equal(2, required);
        Equal(0, actors.GetSnapshot().CurrentActors);

        Equal(ClientActorPresentationSyncStatus.Applied,
            actors.TrySynchronize(cache, presented, metadata, 81, changes,
                out int changeCount, out required));
        Equal(2, changeCount);
        Equal(2, required);
        True(changes.AsSpan(0, changeCount).ToArray().All(
            static change => change.Kind == ClientActorPresentationChangeKind.Added));
        True(actors.TryGet(aliceId, out ClientActorPresentationRecord alice));
        True(actors.TryGet(bobId, out ClientActorPresentationRecord bob));
        Equal("Alice", alice.DisplayLabel);
        Equal(ClientActorVisualKind.HumanX, alice.Definition.VisualKind);
        Equal("Bob", bob.DisplayLabel);
        Equal(ClientActorVisualKind.HumanY, bob.Definition.VisualKind);

        Apply(metadata, bobId, 2, "Bob the Brave");
        Equal(ClientActorPresentationSyncStatus.Applied,
            actors.TrySynchronize(cache, presented, metadata, 81, changes,
                out changeCount, out required));
        Equal(1, changeCount);
        Equal(ClientActorPresentationChangeKind.Updated, changes[0].Kind);
        Equal(bobId, changes[0].Id);
        Equal("Bob the Brave", changes[0].Current.DisplayLabel);

        WorldPublicRecord movedAlice = publicRecords[0] with
        {
            EntityPublicVersion = 2,
            Position = new WorldPosition(key.Coord.X, key.Coord.Z, 1_500, 1_000, 750),
            LastChangedRevision = new ChunkPublicationRevision(2),
        };
        var movement = new ChunkPublicationBatch(
            ChunkPublicationReadStatus.Snapshot,
            key,
            new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(2)),
            new ChunkPublicationRevision(1),
            1,
            1);
        True(cache.TryApplySnapshot(handle, movement, [movedAlice], out _));
        Equal(ClientActorPresentationSyncStatus.Applied,
            actors.TrySynchronize(cache, presented, metadata, 81, changes,
                out changeCount, out required));
        Equal(2, changeCount);
        True(changes.AsSpan(0, changeCount).ToArray().Any(change =>
            change.Kind == ClientActorPresentationChangeKind.Updated && change.Id == aliceId));
        True(changes.AsSpan(0, changeCount).ToArray().Any(change =>
            change.Kind == ClientActorPresentationChangeKind.Removed && change.Id == bobId));
        Equal(1, actors.GetSnapshot().CurrentActors);

        var removed = new ClientPublicPresentationMetadata(81, bobId, 3,
            ClientPublicPresentationFlags.None, string.Empty);
        Equal(ClientPublicPresentationApplyStatus.Applied, metadata.TryApply(
            ClientPublicPresentationOperation.Remove, in removed));
        var stale = new ClientPublicPresentationMetadata(81, bobId, 2,
            ClientPublicPresentationFlags.NameplateEligible, "Stale Bob");
        Equal(ClientPublicPresentationApplyStatus.Rejected, metadata.TryApply(
            ClientPublicPresentationOperation.Present, in stale));
        var nextLifetime = new ClientPublicPresentationMetadata(
            81, new WorldPublicEntityId(bobId.Index, bobId.Generation + 1), 1,
            ClientPublicPresentationFlags.NameplateEligible, "Future Bob");
        Equal(ClientPublicPresentationApplyStatus.Applied, metadata.TryApply(
            ClientPublicPresentationOperation.Present, in nextLifetime));
        False(metadata.TryGet(bobId, out _));
        True(metadata.TryGet(nextLifetime.Id, out _));

        for (int warm = 0; warm < 8; warm++)
            Equal(ClientActorPresentationSyncStatus.NoChanges,
                actors.TrySynchronize(cache, presented, metadata, 81, changes,
                    out _, out _));
        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
            Equal(ClientActorPresentationSyncStatus.NoChanges,
                actors.TrySynchronize(cache, presented, metadata, 81, changes,
                    out _, out _));
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - beforeAllocation);
        ClientActorPresentationStateSnapshot snapshot = actors.GetSnapshot();
        Equal(1, snapshot.CurrentActors);
        True(snapshot.ReservedBytes > 0);
        Equal(2, metadata.GetSnapshot().KnownIdentityFloors);
    }

    private static void Apply(
        ClientPublicPresentationMetadataState state,
        WorldPublicEntityId id,
        ulong revision,
        string displayLabel)
    {
        var value = new ClientPublicPresentationMetadata(
            81, id, revision, ClientPublicPresentationFlags.NameplateEligible, displayLabel);
        Equal(ClientPublicPresentationApplyStatus.Applied, state.TryApply(
            ClientPublicPresentationOperation.Present, in value));
    }

    private static WorldPublicRecord Public(
        WorldPublicEntityId id,
        uint prefab,
        SpatialChunkKey key,
        ushort localX,
        ulong version) => new(
            id,
            version,
            prefab,
            new WorldPosition(key.Coord.X, key.Coord.Z, localX, 1_000, 750),
            WorldYaw.QuarterTurn,
            WorldPublicStateFlags.Visible,
            new ChunkPublicationRevision(version));

    private static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool condition)
    {
        if (condition) throw new InvalidOperationException("Expected false.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
