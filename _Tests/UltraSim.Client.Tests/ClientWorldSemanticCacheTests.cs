using System.Runtime.InteropServices;
using UltraSim.Client.Core;
using UltraSim.Game.World;
using UltraSim.Spatial;

internal static class ClientWorldSemanticCacheTests
{
    private static readonly SpatialSpaceId Space = new(71);
    private static readonly WorldContentFingerprint Content = new(0x7108, 0x20260821);

    internal static void Run()
    {
        BoundWorldPack world = CreateWorld();
        LifecycleAndDeterministicHash(world);
        CapacityFailureIsTransactional(world);
        PublicationKnowledgeIsGlobalAndFenced(world);
        SnapshotLineageAndCanonicality(world);
        PublicationEpochReplacementIsExplicit(world);
        DeterministicClientCacheChurnMatchesReference(world);
    }

    private static void LifecycleAndDeterministicHash(BoundWorldPack world)
    {
        ClientWorldSemanticCacheLimits limits = new(3, 4, 4, 4, 8);
        var first = new ClientWorldSemanticCache(world, limits);
        var second = new ClientWorldSemanticCache(world, limits);
        SpatialChunkKey a = Key(0, 0);
        SpatialChunkKey b = Key(1, 0);
        PublicationEpoch epoch = PublicationEpoch.Create();
        WorldPublicRecord publicA = Public(2, 1, 1, a, 1_000);

        ClientSemanticChunkHandle firstA = Publish(first, a, epoch, 1, [publicA]);
        ClientSemanticChunkHandle firstB = Publish(first, b, epoch, 1, []);
        _ = Publish(second, b, epoch, 1, []);
        _ = Publish(second, a, epoch, 1, [publicA]);

        ClientWorldSemanticCacheSnapshot firstSnapshot = first.GetSnapshot();
        Equal(2, firstSnapshot.ResidentChunks);
        Equal(firstSnapshot.WorldSemanticHash, second.GetSnapshot().WorldSemanticHash);
        True(first.TryGetResidentView(firstA, out ClientSemanticChunkView view));
        Equal(TerrainChunkFormat.MaterialCellCount, view.TerrainMaterials.Length);
        Equal(TerrainChunkFormat.HeightVertexCount, view.TerrainAbsoluteHeightsMillimeters.Length);
        Equal(1, view.FrozenStatics.Length);
        Equal(1, view.MultiFragments.Length);
        Equal(publicA, view.PublicRecords[0]);
        True(firstSnapshot.ReservedBytes > 0);

        True(first.BeginRetire(firstA));
        False(first.TryRequest(a, out _, out ClientSemanticChunkError retiringError));
        Equal(ClientSemanticChunkError.InvalidState, retiringError);
        True(first.CompleteRetire(firstA));
        True(first.TryRequest(Key(-1, 0), out ClientSemanticChunkHandle replacement, out _));
        Equal(firstA.Index, replacement.Index);
        True(replacement.Generation > firstA.Generation);
        False(first.TryGetInfo(firstA, out _));
        True(first.TryGetInfo(firstB, out _));
    }

    private static void CapacityFailureIsTransactional(BoundWorldPack world)
    {
        var cache = new ClientWorldSemanticCache(world, new(1, 0, 1, 1, 1));
        True(cache.TryRequest(Key(0, 0), out ClientSemanticChunkHandle handle, out _));
        False(cache.TryStageFromBaseWorld(handle, [], default, out ClientSemanticChunkError error));
        Equal(ClientSemanticChunkError.FrozenStaticCapacityExceeded, error);
        True(cache.TryGetInfo(handle, out ClientSemanticChunkInfo info));
        Equal(ClientSemanticChunkState.Requested, info.State);
        Equal(default(ClientSemanticHash), info.SemanticHash);
        False(cache.TryRequest(Key(1, 0), out _, out error));
        Equal(ClientSemanticChunkError.CapacityExceeded, error);
    }

    private static void PublicationKnowledgeIsGlobalAndFenced(BoundWorldPack world)
    {
        var cache = new ClientWorldSemanticCache(world, new(2, 2, 2, 4, 8));
        SpatialChunkKey a = Key(0, 0);
        SpatialChunkKey b = Key(1, 0);
        PublicationEpoch epoch = PublicationEpoch.Create();
        WorldPublicRecord a1 = Public(5, 1, 1, a, 200);
        ClientSemanticChunkHandle handleA = Publish(cache, a, epoch, 1, [a1]);
        ClientSemanticChunkHandle handleB = Publish(cache, b, epoch, 1, []);

        WorldPublicDeltaRecord removeA = new(
            WorldPublicOperation.Remove, WorldPublicFieldMask.None,
            a1.Id, 2, default);
        True(cache.TryApplyDelta(handleA, DeltaBatch(a, epoch, 2, 1), [removeA], out _));
        Equal(0, PublicCount(cache, handleA));

        WorldPublicRecord b3 = Public(5, 1, 3, b, 400) with
        {
            LastChangedRevision = new ChunkPublicationRevision(2),
        };
        WorldPublicDeltaRecord presentB = new(
            WorldPublicOperation.Present, WorldPublicFieldMask.Complete,
            b3.Id, b3.EntityPublicVersion, b3);
        True(cache.TryApplyDelta(handleB, DeltaBatch(b, epoch, 2, 1), [presentB], out _));
        Equal(1, PublicCount(cache, handleB));

        WorldPublicRecord delayedA = Public(5, 1, 2, a, 300);
        WorldPublicDeltaRecord stale = new(
            WorldPublicOperation.Present, WorldPublicFieldMask.Complete,
            delayedA.Id, delayedA.EntityPublicVersion, delayedA);
        True(cache.TryApplyDelta(handleA, DeltaBatch(a, epoch, 3, 1), [stale], out _));
        Equal(0, PublicCount(cache, handleA));
        Equal(1, PublicCount(cache, handleB));

        var delayedSnapshotBatch = new ChunkPublicationBatch(
            ChunkPublicationReadStatus.Snapshot, a,
            new ChunkPublicationCursor(epoch, a, new ChunkPublicationRevision(4)),
            new ChunkPublicationRevision(1), 1, 1);
        True(cache.TryApplySnapshot(handleA, delayedSnapshotBatch, [delayedA], out _));
        Equal(0, PublicCount(cache, handleA));

        WorldPublicDeltaRecord wrongChunkRemove = new(
            WorldPublicOperation.Remove, WorldPublicFieldMask.None, b3.Id, 4, default);
        False(cache.TryApplyDelta(handleA, DeltaBatch(a, epoch, 5, 1),
            [wrongChunkRemove], out ClientSemanticChunkError wrongChunkError));
        Equal(ClientSemanticChunkError.ContradictoryPublication, wrongChunkError);

        ClientSemanticHash before = Info(cache, handleB).SemanticHash;
        WorldPublicRecord contradiction = b3 with { PrefabId = 999 };
        WorldPublicDeltaRecord conflicting = presentB with { Current = contradiction };
        False(cache.TryApplyDelta(handleB, DeltaBatch(b, epoch, 3, 1), [conflicting], out ClientSemanticChunkError error));
        Equal(ClientSemanticChunkError.ContradictoryPublication, error);
        Equal(before, Info(cache, handleB).SemanticHash);

        WorldPublicRecord generation2 = Public(5, 2, 1, a, 500);
        WorldPublicDeltaRecord replace = new(
            WorldPublicOperation.Present, WorldPublicFieldMask.Complete,
            generation2.Id, generation2.EntityPublicVersion, generation2);
        True(cache.TryApplyDelta(handleA, DeltaBatch(a, epoch, 6, 1), [replace], out _));
        Equal(1, PublicCount(cache, handleA));
        Equal(0, PublicCount(cache, handleB));

        var absent = new ChunkPublicationBatch(
            ChunkPublicationReadStatus.ChunkAbsent, a, default, default, 0, 0);
        True(cache.TryApplyChunkAbsent(handleA, epoch, absent, out _));
        Equal(0, PublicCount(cache, handleA));
    }

    private static void PublicationEpochReplacementIsExplicit(BoundWorldPack world)
    {
        var cache = new ClientWorldSemanticCache(world, new(1, 2, 2, 2, 4));
        SpatialChunkKey key = Key(0, 0);
        PublicationEpoch oldEpoch = PublicationEpoch.Create();
        ClientSemanticChunkHandle handle = Publish(cache, key, oldEpoch, 1,
            [Public(1, 1, 1, key, 10)]);
        PublicationEpoch newEpoch = PublicationEpoch.Create();
        WorldPublicRecord replacement = Public(1, 1, 1, key, 20);
        var batch = new ChunkPublicationBatch(
            ChunkPublicationReadStatus.Snapshot, key,
            new ChunkPublicationCursor(newEpoch, key, new ChunkPublicationRevision(1)),
            new ChunkPublicationRevision(1), 1, 1);
        False(cache.TryApplySnapshot(handle, batch, [replacement], out ClientSemanticChunkError error));
        Equal(ClientSemanticChunkError.InvalidPublication, error);
        cache.ResetPublicationEpoch(newEpoch);
        True(cache.TryApplySnapshot(handle, batch, [replacement], out _));
        Equal(newEpoch, cache.GetSnapshot().PublicationEpoch);
        Equal(replacement, Resident(cache, handle).PublicRecords[0]);
    }

    private static void SnapshotLineageAndCanonicality(BoundWorldPack world)
    {
        var cache = new ClientWorldSemanticCache(world, new(2, 2, 2, 4, 8));
        SpatialChunkKey key = Key(0, 0);
        PublicationEpoch epoch = PublicationEpoch.Create();
        WorldPublicRecord canonical = Public(8, 1, 5, key, 700);
        ClientSemanticChunkHandle handle = Publish(cache, key, epoch, 5, [canonical]);
        ClientSemanticHash before = Info(cache, handle).SemanticHash;
        var equal = new ChunkPublicationBatch(
            ChunkPublicationReadStatus.Snapshot, key,
            new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(5)),
            new ChunkPublicationRevision(1), 1, 1);
        True(cache.TryApplySnapshot(handle, equal, [canonical], out _));
        Equal(before, Info(cache, handle).SemanticHash);

        var contradictory = equal with { Count = 0 };
        False(cache.TryApplySnapshot(handle, contradictory, [], out ClientSemanticChunkError conflict));
        Equal(ClientSemanticChunkError.ContradictoryPublication, conflict);
        Equal(canonical, Resident(cache, handle).PublicRecords[0]);

        var missingCursor = equal with { Cursor = default };
        False(cache.TryApplySnapshot(handle, missingCursor, [canonical], out ClientSemanticChunkError missing));
        Equal(ClientSemanticChunkError.InvalidPublication, missing);

        SpatialChunkKey second = Key(1, 0);
        True(cache.TryRequest(second, out ClientSemanticChunkHandle staged, out _));
        WorldPublicRecord secondRecord = Public(9, 1, 1, second, 800);
        False(cache.TryStageFromBaseWorld(staged, [secondRecord], default,
            out ClientSemanticChunkError stagedMissing));
        Equal(ClientSemanticChunkError.InvalidPublication, stagedMissing);

        var next = equal with
        {
            Cursor = new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(6)),
        };
        WorldPublicRecord unknownFlags = canonical with
        {
            EntityPublicVersion = 6,
            StateFlags = (WorldPublicStateFlags)0x8000,
            LastChangedRevision = new ChunkPublicationRevision(6),
        };
        False(cache.TryApplySnapshot(handle, next, [unknownFlags], out ClientSemanticChunkError flags));
        Equal(ClientSemanticChunkError.InvalidPublication, flags);
        WorldPublicRecord zeroRevision = unknownFlags with
        {
            StateFlags = WorldPublicStateFlags.Visible,
            LastChangedRevision = default,
        };
        False(cache.TryApplySnapshot(handle, next, [zeroRevision], out ClientSemanticChunkError zero));
        Equal(ClientSemanticChunkError.InvalidPublication, zero);
        WorldPublicRecord futureRevision = zeroRevision with
        {
            LastChangedRevision = new ChunkPublicationRevision(7),
        };
        False(cache.TryApplySnapshot(handle, next, [futureRevision], out ClientSemanticChunkError future));
        Equal(ClientSemanticChunkError.InvalidPublication, future);
        Equal(before, Info(cache, handle).SemanticHash);
    }

    private static void DeterministicClientCacheChurnMatchesReference(BoundWorldPack world)
    {
        const int operations = 5_000;
        var cache = new ClientWorldSemanticCache(world, new(4, 2, 2, 4, 64));
        var handles = new Dictionary<SpatialChunkKey, ClientSemanticChunkHandle>();
        var expected = new Dictionary<SpatialChunkKey, List<WorldPublicRecord>>();
        var revisions = new Dictionary<SpatialChunkKey, ulong>();
        var versions = new ulong[64];
        Array.Fill(versions, 1UL);
        PublicationEpoch epoch = PublicationEpoch.Create();
        uint random = 0x7A08_2026;
        uint sharedGeneration = 1;

        Admit(Key(-1, 0));
        Admit(Key(0, 0));
        Admit(Key(1, 0));
        Admit(Key(-1, 1));
        for (int operation = 0; operation < operations; operation++)
        {
            random = unchecked(random * 1_664_525U + 1_013_904_223U);
            SpatialChunkKey key = handles.Keys.Order().ElementAt((int)(random % (uint)handles.Count));
            ClientSemanticChunkHandle handle = handles[key];
            switch (operation % 8)
            {
                case 0:
                case 1:
                {
                    uint identityIndex = checked((uint)((key.Coord.X + 1) * 3 + key.Coord.Z + 2));
                    ulong version = ++versions[identityIndex];
                    WorldPublicRecord record = Public(identityIndex, 1, version, key,
                        checked((ushort)(500 + version % 4_000)));
                    ApplyPresent(key, handle, record);
                    break;
                }
                case 2:
                {
                    if (!Info(cache, handle).PublicationCursor.IsValid) break;
                    WorldPublicRecord[] rows = expected[key].OrderBy(value => value.Id).ToArray();
                    ChunkPublicationCursor cursor = Info(cache, handle).PublicationCursor;
                    var batch = new ChunkPublicationBatch(
                        ChunkPublicationReadStatus.Snapshot, key, cursor,
                        new ChunkPublicationRevision(1), rows.Length, rows.Length);
                    True(cache.TryApplySnapshot(handle, batch, rows, out _));
                    if (rows.Length != 0)
                    {
                        ClientSemanticHash before = Info(cache, handle).SemanticHash;
                        WorldPublicRecord[] conflict = (WorldPublicRecord[])rows.Clone();
                        conflict[0] = conflict[0] with { PrefabId = conflict[0].PrefabId + 1 };
                        False(cache.TryApplySnapshot(handle, batch, conflict,
                            out ClientSemanticChunkError contradiction));
                        Equal(ClientSemanticChunkError.ContradictoryPublication, contradiction);
                        Equal(before, Info(cache, handle).SemanticHash);
                    }
                    break;
                }
                case 3:
                {
                    var absent = new ChunkPublicationBatch(
                        ChunkPublicationReadStatus.ChunkAbsent, key, default, default, 0, 0);
                    True(cache.TryApplyChunkAbsent(handle, epoch, absent, out _));
                    expected[key].Clear();
                    revisions[key] = 0;
                    break;
                }
                case 4:
                {
                    WorldPublicRecord shared = Public(60, ++sharedGeneration, 1, key,
                        checked((ushort)(1_000 + sharedGeneration % 5_000)));
                    ApplyPresent(key, handle, shared);
                    foreach (List<WorldPublicRecord> page in expected.Values)
                        page.RemoveAll(value => value.Id.Index == shared.Id.Index && value.Id != shared.Id);
                    break;
                }
                case 5:
                {
                    True(cache.BeginRetire(handle));
                    True(cache.CompleteRetire(handle));
                    handles.Remove(key);
                    expected.Remove(key);
                    revisions.Remove(key);
                    for (int candidate = -1; candidate <= 1; candidate++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            SpatialChunkKey replacement = Key(candidate, z);
                            if (!handles.ContainsKey(replacement))
                            {
                                Admit(replacement);
                                candidate = 2;
                                break;
                            }
                        }
                    }
                    False(cache.TryGetInfo(handle, out _));
                    break;
                }
                case 6:
                {
                    SpatialChunkKey unavailable = Key(1, 1);
                    if (handles.ContainsKey(unavailable)) unavailable = Key(-1, -1);
                    if (!handles.ContainsKey(unavailable))
                    {
                        ClientSemanticHash before = cache.GetSnapshot().WorldSemanticHash;
                        False(cache.TryRequest(unavailable, out _, out ClientSemanticChunkError capacity));
                        Equal(ClientSemanticChunkError.CapacityExceeded, capacity);
                        Equal(before, cache.GetSnapshot().WorldSemanticHash);
                    }
                    break;
                }
                case 7:
                    if (operation != 0 && operation % 127 == 0)
                    {
                        epoch = PublicationEpoch.Create();
                        cache.ResetPublicationEpoch(epoch);
                        foreach (SpatialChunkKey resident in handles.Keys.ToArray())
                        {
                            expected[resident].Clear();
                            revisions[resident] = 0;
                        }
                    }
                    break;
            }
            AssertReference();
        }

        void Admit(SpatialChunkKey key)
        {
            True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
            True(cache.TryStageFromBaseWorld(handle, [], default, out _));
            True(cache.TryPublish(handle, out _));
            handles.Add(key, handle);
            expected.Add(key, []);
            revisions.Add(key, 0);
        }

        void ApplyPresent(
            SpatialChunkKey key,
            ClientSemanticChunkHandle handle,
            WorldPublicRecord record)
        {
            ulong revision = ++revisions[key];
            record = record with { LastChangedRevision = new ChunkPublicationRevision(revision) };
            foreach (List<WorldPublicRecord> retained in expected.Values)
                retained.RemoveAll(value => value.Id.Index == record.Id.Index);
            List<WorldPublicRecord> page = expected[key];
            page.Add(record);
            page.Sort((left, right) => left.Id.CompareTo(right.Id));
            ClientSemanticChunkInfo info = Info(cache, handle);
            if (!info.PublicationCursor.IsValid)
            {
                var batch = new ChunkPublicationBatch(
                    ChunkPublicationReadStatus.Snapshot, key,
                    new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(revision)),
                    new ChunkPublicationRevision(revision), page.Count, page.Count);
                True(cache.TryApplySnapshot(handle, batch, CollectionsMarshal.AsSpan(page), out _));
            }
            else
            {
                var delta = new WorldPublicDeltaRecord(
                    WorldPublicOperation.Present, WorldPublicFieldMask.Complete,
                    record.Id, record.EntityPublicVersion, record);
                True(cache.TryApplyDelta(handle, DeltaBatch(key, epoch, revision, 1), [delta], out _));
            }
        }

        void AssertReference()
        {
            Equal(handles.Count, cache.GetSnapshot().ResidentChunks);
            foreach ((SpatialChunkKey key, ClientSemanticChunkHandle handle) in handles)
            {
                True(cache.TryGetResidentView(handle, out ClientSemanticChunkView view));
                WorldPublicRecord[] rows = expected[key].OrderBy(value => value.Id).ToArray();
                Equal(rows.Length, view.PublicRecords.Length);
                for (int row = 0; row < rows.Length; row++) Equal(rows[row], view.PublicRecords[row]);
            }
        }
    }

    private static ClientSemanticChunkHandle Publish(
        ClientWorldSemanticCache cache,
        SpatialChunkKey key,
        PublicationEpoch epoch,
        ulong revision,
        ReadOnlySpan<WorldPublicRecord> records)
    {
        True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
        var cursor = new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(revision));
        True(cache.TryStageFromBaseWorld(handle, records, cursor, out _));
        True(cache.TryPublish(handle, out _));
        return handle;
    }

    private static ChunkPublicationBatch DeltaBatch(
        SpatialChunkKey key,
        PublicationEpoch epoch,
        ulong revision,
        int count) => new(
            ChunkPublicationReadStatus.Delta,
            key,
            new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(revision)),
            new ChunkPublicationRevision(1),
            count,
            count);

    private static ClientSemanticChunkInfo Info(
        ClientWorldSemanticCache cache,
        ClientSemanticChunkHandle handle)
    {
        True(cache.TryGetInfo(handle, out ClientSemanticChunkInfo info));
        return info;
    }

    private static int PublicCount(ClientWorldSemanticCache cache, ClientSemanticChunkHandle handle) =>
        Info(cache, handle).PublicRecordCount;

    private static ClientSemanticChunkView Resident(
        ClientWorldSemanticCache cache,
        ClientSemanticChunkHandle handle)
    {
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView view));
        return view;
    }

    private static WorldPublicRecord Public(
        uint index,
        uint generation,
        ulong version,
        SpatialChunkKey key,
        ushort localX) => new(
            new WorldPublicEntityId(index, generation),
            version,
            20,
            new WorldPosition(key.Coord.X, key.Coord.Z, localX, 600, 750),
            WorldYaw.QuarterTurn,
            WorldPublicStateFlags.Visible,
            new ChunkPublicationRevision(version));

    private static BoundWorldPack CreateWorld()
    {
        var builder = new WorldPackBuilder(
            Space, Content, new WorldPackLimits(2 * 1024 * 1024, 9, 8, 8, 4, 8));
        for (int z = -1; z <= 1; z++)
        {
            for (int x = -1; x <= 1; x++)
            {
                ushort[] materials = new ushort[TerrainChunkFormat.MaterialCellCount];
                Array.Fill(materials, checked((ushort)(((x + z) & 1) + 1)));
                var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
                True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
                True(builder.TryAddBaseTerrain(Key(x, z), materials, heightBase, offsets, out _));
            }
        }
        WorldPosition staticPosition = new(0, 0, 1_000, 1_000, 0);
        True(builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
            new WorldStaticId(1), new WorldStaticDefinitionId(1), staticPosition,
            WorldYaw.Zero, 0, WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
            Bounds(staticPosition, 250)), out _));
        WorldPosition multiPosition = new(0, 0, 2_000, 2_000, 0);
        WorldMultiPieceSource[] pieces =
        [
            new(new MultiPieceId(1), new WorldStaticDefinitionId(2), multiPosition,
                WorldYaw.QuarterTurn, 0, WorldStaticFlags.Renderable, Bounds(multiPosition, 300)),
        ];
        True(builder.TryAddStaticMulti(new MultiInstanceId(1), multiPosition,
            WorldYaw.Zero, pieces, out _));
        True(builder.TryBuild(out WorldPackArtifact? artifact, out _));
        WorldPack pack = artifact!.Open();
        var binding = new WorldContentBinding(Content, [1, 2], [1, 2]);
        True(pack.TryBind(binding, out BoundWorldPack? bound, out _));
        return bound!;
    }

    private static WorldStaticBounds Bounds(WorldPosition position, int half) => new(
        position.GlobalXMillimeters - half,
        position.YMillimeters - half,
        position.GlobalZMillimeters - half,
        position.GlobalXMillimeters + half,
        position.YMillimeters + half,
        position.GlobalZMillimeters + half);

    private static SpatialChunkKey Key(int x, int z) => new(Space, x, z);

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
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
