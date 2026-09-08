using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;

public partial class T010PlayableWorldAcceptance : Node3D
{
    public override void _Ready()
    {
        try
        {
            RunAcceptance();
            RunJoinedInputAuthorityAcceptance();
            RunRemotePublicActorAcceptance();
            RunControlledActorOwnerPoseAcceptance();
            GD.Print("T0.10 GODOT PLAYABLE WORLD ACCEPTANCE PASS");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"T0.10 GODOT PLAYABLE WORLD ACCEPTANCE FAIL: {exception}");
            GetTree().Quit(1);
        }
    }

    private void RunControlledActorOwnerPoseAcceptance()
    {
        WorldPublicEntityId controlledId = new(701, 3);
        WorldPublicEntityId otherId = new(702, 4);
        ClientActorDefinitionCatalog catalog = ClientActorDefinitionCatalog.ConceptDemo;
        if (!catalog.TryGet(
                GameWorldPublicPrefabIds.Character, out ClientActorDefinition controlledDefinition) ||
            !catalog.TryGet(
                GameWorldPublicPrefabIds.CharacterMale, out ClientActorDefinition otherDefinition))
            throw new InvalidOperationException(
                "The controlled-pose fixture could not resolve its actor definitions.");
        if (!WorldPosition.TryFromGlobalMillimeters(
                1_000, 1_000, 1_000, out WorldPosition staleControlled) ||
            !WorldPosition.TryFromGlobalMillimeters(
                2_000, 1_300, 2_000, out WorldPosition ownerPose) ||
            !WorldPosition.TryFromGlobalMillimeters(
                4_000, 1_000, 5_000, out WorldPosition otherPosition))
            throw new InvalidOperationException(
                "The controlled-pose fixture positions were invalid.");

        ClientActorPresentationChange[] additions =
        [
            new(
                ClientActorPresentationChangeKind.Added,
                controlledId,
                new ClientActorPresentationRecord(
                    controlledId,
                    GameWorldPublicPrefabIds.Character,
                    staleControlled,
                    WorldYaw.Zero,
                    controlledDefinition,
                    "Controlled",
                    true)),
            new(
                ClientActorPresentationChangeKind.Added,
                otherId,
                new ClientActorPresentationRecord(
                    otherId,
                    GameWorldPublicPrefabIds.CharacterMale,
                    otherPosition,
                    WorldYaw.QuarterTurn,
                    otherDefinition,
                    "Other",
                    true)),
        ];

        using var presenter = new GodotActorPresenter(this, catalog, 2);
        presenter.SetControlledActorPose(controlledId, ownerPose, WorldYaw.HalfTurn);
        presenter.Apply(additions, ownerPose, nowMilliseconds: 1);
        if (!presenter.TryGetActorRoot(controlledId, out Node3D? controlled) || controlled is null)
            throw new InvalidOperationException("The controlled actor root was not realized.");
        Expect(controlled.Position.IsEqualApprox(Vector3.Zero) &&
            Mathf.IsEqualApprox(controlled.Rotation.Y, MathF.PI),
            "A stale public record displaced or rotated the controlled actor away from its fresh owner pose.");
        if (!presenter.TryGetActorRoot(otherId, out Node3D? other) || other is null)
            throw new InvalidOperationException("The other actor root was not realized.");
        Expect(other.Position.IsEqualApprox(new Vector3(2f, -0.3f, 3f)) &&
            Mathf.IsEqualApprox(other.Rotation.Y, MathF.PI * 0.5f),
            "The owner-pose override contaminated ordinary public actor presentation.");

        Expect(WorldPosition.TryFromGlobalMillimeters(
                2_200, 1_500, 2_300, out WorldPosition nextOwnerPose),
            "The next controlled owner pose was invalid.");
        presenter.SetControlledActorPose(
            controlledId, nextOwnerPose, WorldYaw.ThreeQuarterTurn);
        presenter.UpdateOriginAndLifetimes(nextOwnerPose, nowMilliseconds: 2);
        Expect(controlled.Position.IsEqualApprox(Vector3.Zero) &&
            Mathf.IsEqualApprox(controlled.Rotation.Y, MathF.PI * 1.5f),
            "Owner movement did not keep the controlled actor pinned to the player-relative origin.");
        Expect(other.Position.IsEqualApprox(new Vector3(1.8f, -0.5f, 2.7f)),
            "An ordinary public actor stopped following the player-relative render origin.");
    }

    private void RunJoinedInputAuthorityAcceptance()
    {
        GodotWorldInputMap map = GodotWorldInputMap.Default;
        Expect(InputMap.HasAction(map.MoveLeft) && InputMap.HasAction(map.MoveRight) &&
            InputMap.HasAction(map.MoveForward) && InputMap.HasAction(map.MoveBackward) &&
            InputMap.HasAction(map.Interact) && InputMap.HasAction(map.Primary),
            "The native project does not declare the playable InputMap contract.");

        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        BoundWorldPack bound = BindPlayablePack(pack);
        var composition = new UltraSimGameComposition(
            new UltraSim.Game.Inventory.InventoryLimits(32, 128, 32),
            baseWorldPack: pack);
        var options = new ServerRuntimeOptions(
            UltraSimGameComposition.CreateWorldOptions(
                128,
                new PersistenceIdentityOptions(new PersistentWorldId(0x80_3010))),
            WorldClockOptions.Default);
        using var runtime = new ServerRuntime(composition, options);
        runtime.Initialize();
        runtime.Start();
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(new ParticipantId(3010));
        Expect(entered.IsEntered, "The joined native actor could not enter authority.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
            "The joined native world could not populate.");
        runtime.Tick();

        var cache = new ClientWorldSemanticCache(
            bound,
            new ClientWorldSemanticCacheLimits(14, 16, 16, 16, 64));
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (_, _, game) =>
            {
                StageAuthoritativeWindow(game.WorldState.Publications, cache,
                    GamePlayableWorld.PlayerStart.Chunk);
                return true;
            }, out bool staged) && staged,
            "The joined native cache did not consume authoritative publication snapshots.");

        using var renderer = new GodotWorldRenderer(
            this,
            cache,
            GodotWorldRendererLimits.ForStructuralWindow(1, 5, 16));
        var window = new GodotSlidingRenderWindow(cache, renderer, 1);
        var changes = new ClientPresentationChange[18];
        Expect(window.TryTrackPlayer(
            GamePlayableWorld.PlayerStart,
            changes,
            out _, out _, out _, out _),
            "The joined native presentation window could not initialize.");
        _ = Drain(renderer);

        var sequencer = new ClientWorldInputSequencer();
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, _, game) => game.WorldActions.BeginControlSession(
                world, entities.Player, sequencer.ControlSession),
            out GameWorldActionStatus controlBound) && controlBound == GameWorldActionStatus.Accepted,
            "The joined native control session could not bind to the actor.");
        try
        {
            Input.ActionPress(map.MoveRight, 1f);
            Input.ActionPress(map.Interact, 1f);
            ClientWorldInputSample sample = GodotWorldInput.Capture(map, windowHasFocus: true);
            Expect(sequencer.TryCreateCommand(sample, out GamePlayerCommand command),
                "Godot input did not form the shared player command.");
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayerCommandResult>(
                (world, _, game) =>
                {
                    game.WorldActions.ApplyPlayerCommand(
                        world,
                        game.WorldState,
                        entities.Player,
                        command,
                        entities.CabinDoor,
                        out GamePlayerCommandResult result);
                    return result;
                }, out GamePlayerCommandResult authority) && authority.WasAdmitted &&
                authority.Locomotion == GameWorldActionStatus.Accepted &&
                authority.Interaction == GameWorldActionStatus.Accepted,
                "Godot input did not reach composed movement/interaction authority.");
        }
        finally
        {
            Input.ActionRelease(map.MoveRight);
            Input.ActionRelease(map.Interact);
        }
        runtime.Tick();

        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, WorldPosition>(
            (world, _, game) =>
            {
                Expect(game.WorldActions.TryReadCabinDoor(world, entities.CabinDoor, out bool open) && open,
                    "The joined native door authority did not open.");
                Expect(game.WorldState.TryReadPosition(world, entities.Player, out WorldPosition position),
                    "The joined native player position was missing.");
                RefreshAuthoritativeChunk(game.WorldState.Publications, cache,
                    GamePlayableWorld.PlayerStart.ToChunkKey(GameWorldState.DefaultSpace));
                RefreshAuthoritativeChunk(game.WorldState.Publications, cache,
                    GamePlayableWorld.DoorPosition.ToChunkKey(GameWorldState.DefaultSpace));
                return position;
            }, out WorldPosition moved),
            "The joined native authority/publication refresh failed.");
        Expect(moved.GlobalXMillimeters ==
            GamePlayableWorld.PlayerStart.GlobalXMillimeters + 100,
            "The joined native 60 Hz movement did not integrate to 100 mm.");

        SpatialChunkKey playerKey = GamePlayableWorld.PlayerStart.ToChunkKey(GameWorldState.DefaultSpace);
        SpatialChunkKey doorKey = GamePlayableWorld.DoorPosition.ToChunkKey(GameWorldState.DefaultSpace);
        Expect(cache.TryGetResidentHandle(playerKey, out ClientSemanticChunkHandle playerHandle) &&
            cache.TryGetResidentHandle(doorKey, out ClientSemanticChunkHandle doorHandle) &&
            renderer.TryQueuePublicRefresh(playerHandle, out _) &&
            renderer.TryQueuePublicRefresh(doorHandle, out _),
            "The joined native public presentation refresh did not queue.");
        renderer.ProcessFrame(new GodotWorldFrameBudget(2, 0));
        Expect(window.TryTrackPlayer(moved, changes, out _, out _, out _, out _),
            "The joined native player position did not reach presentation.");
        GodotWorldRendererSnapshot snapshot = renderer.GetSnapshot();
        Expect(snapshot.PublicRefreshes == 2 && snapshot.ActiveRenderChunks == 9,
            "The joined native renderer did not realize authoritative public updates.");

        GD.Print(
            $"T0.10 JOINED INPUT METRICS commands={sequencer.Snapshot().CommandsProduced} " +
            $"player_x_mm={moved.GlobalXMillimeters} public_refreshes={snapshot.PublicRefreshes} " +
            $"active_chunks={snapshot.ActiveRenderChunks}");
    }

    private static BoundWorldPack BindPlayablePack(WorldPack pack)
    {
        var binding = new WorldContentBinding(
            GameContent.WorldFingerprint,
            [
                GameTerrainMaterialIds.Water,
                GameTerrainMaterialIds.Sand,
                GameTerrainMaterialIds.Grass,
                GameTerrainMaterialIds.Rock,
                GameTerrainMaterialIds.Snow,
            ],
            [
                GameWorldStaticDefinitionIds.Tree,
                GameWorldStaticDefinitionIds.Rock,
                GameWorldStaticDefinitionIds.CabinWall,
                GameWorldStaticDefinitionIds.CabinRoof,
                GameWorldStaticDefinitionIds.Shelf,
                GameWorldStaticDefinitionIds.Book,
            ]);
        Expect(pack.TryBind(binding, out BoundWorldPack? bound, out _),
            "The authored playable pack did not bind to game content.");
        return bound!;
    }

    private static void StageAuthoritativeWindow(
        ChunkPublicationStore publications,
        ClientWorldSemanticCache cache,
        SpatialChunkCoord center)
    {
        Span<WorldPublicRecord> rows = stackalloc WorldPublicRecord[16];
        for (int z = center.Z - 1; z <= center.Z + 1; z++)
        {
            for (int x = center.X - 1; x <= center.X + 1; x++)
            {
                SpatialChunkKey key = Key(x, z);
                Expect(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _),
                    $"The authoritative native chunk {key} could not be requested.");
                ChunkPublicationReadStatus status = publications.TryCopySnapshot(
                    key, rows, out ChunkPublicationBatch batch);
                bool staged = status == ChunkPublicationReadStatus.Snapshot
                    ? cache.TryStageFromBaseWorld(handle, rows[..batch.Count], batch.Cursor, out _)
                    : status == ChunkPublicationReadStatus.ChunkAbsent &&
                        cache.TryStageFromBaseWorld(handle, [], default, out _);
                Expect(staged && cache.TryPublish(handle, out _),
                    $"The authoritative native chunk {key} could not publish locally.");
            }
        }
    }

    private static void RefreshAuthoritativeChunk(
        ChunkPublicationStore publications,
        ClientWorldSemanticCache cache,
        SpatialChunkKey key)
    {
        Expect(cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle),
            $"The authoritative native refresh key {key} was not resident.");
        Span<WorldPublicRecord> rows = stackalloc WorldPublicRecord[16];
        ChunkPublicationReadStatus status = publications.TryCopySnapshot(
            key, rows, out ChunkPublicationBatch batch);
        Expect(status == ChunkPublicationReadStatus.Snapshot &&
            cache.TryApplySnapshot(handle, batch, rows[..batch.Count], out _),
            $"The authoritative native refresh for {key} failed.");
    }

    private void RunAcceptance()
    {
        ValidateGodotGeometryConventions();
        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        var binding = new WorldContentBinding(
            GameContent.WorldFingerprint,
            [
                GameTerrainMaterialIds.Water,
                GameTerrainMaterialIds.Sand,
                GameTerrainMaterialIds.Grass,
                GameTerrainMaterialIds.Rock,
                GameTerrainMaterialIds.Snow,
            ],
            [
                GameWorldStaticDefinitionIds.Tree,
                GameWorldStaticDefinitionIds.Rock,
                GameWorldStaticDefinitionIds.CabinWall,
                GameWorldStaticDefinitionIds.CabinRoof,
                GameWorldStaticDefinitionIds.Shelf,
                GameWorldStaticDefinitionIds.Book,
            ]);
        Expect(pack.TryBind(binding, out BoundWorldPack? bound, out _),
            "The authored playable pack did not bind to game content.");
        var cache = new ClientWorldSemanticCache(
            bound!,
            new ClientWorldSemanticCacheLimits(14, 16, 16, 16, 64));
        PublicationEpoch epoch = PublicationEpoch.Create();
        WorldPosition player = GamePlayableWorld.PlayerStart;
        StageWindow(cache, epoch, player.Chunk);

        double resourceBaseline = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        double bufferBaseline = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        using var renderer = new GodotWorldRenderer(
            this,
            cache,
            GodotWorldRendererLimits.ForStructuralWindow(1, 5, 16));
        var window = new GodotSlidingRenderWindow(cache, renderer, 1);
        var changes = new ClientPresentationChange[18];
        Expect(window.TryTrackPlayer(
            player, changes, out ClientPresentationWindowTransition initial,
            out _, out ClientPresentationWindowError initialError,
            out GodotWorldRenderQueueError initialRendererError),
            $"Initial native window failed: {initialError}/{initialRendererError}.");
        Expect(initial.Entered == 9 && initial.ActiveChunks == 9,
            "The native playable window was not a complete 3x3.");
        int frames = Drain(renderer);
        GodotWorldRendererSnapshot initialRender = renderer.GetSnapshot();
        Expect(initialRender.ActiveRenderChunks == 9 && initialRender.CompletedBuilds == 9,
            "The authored terrain/static/Multi/public window did not render.");
        ValidateGodotTerrainFrontFaces(renderer.Root);
        Expect(initialRender.ActiveNodes < 9 * TerrainChunkFormat.MaterialCellCount,
            "The playable renderer created cell-shaped scene-tree identity.");

        for (int z = -1; z <= 1; z++) StageIfMissing(cache, epoch, Key(2, z));
        WorldPosition crossed = GamePlayableWorld.ChestPosition;
        Expect(window.TryTrackPlayer(
            crossed, changes, out ClientPresentationWindowTransition crossing,
            out int required, out ClientPresentationWindowError crossingError,
            out GodotWorldRenderQueueError crossingRendererError),
            $"Native chunk crossing failed: {crossingError}/{crossingRendererError}.");
        Expect(crossing.Entered == 3 && crossing.Retained == 6 && crossing.Exited == 3,
            "The native playable crossing was not 3/6/3.");
        frames += Drain(renderer);
        for (int index = 0; index < required; index++)
        {
            if (changes[index].Kind != ClientPresentationChangeKind.Exit) continue;
            Expect(cache.BeginRetire(changes[index].Handle) &&
                cache.CompleteRetire(changes[index].Handle),
                "Exited native semantics could not retire.");
        }

        SpatialChunkKey doorKey = GamePlayableWorld.DoorPosition.ToChunkKey(GameWorldState.DefaultSpace);
        Expect(cache.TryGetResidentHandle(doorKey, out ClientSemanticChunkHandle doorHandle),
            "The rendered cabin-door chunk was not resident.");
        WorldPublicRecord openDoor = Public(
            2, 2, GameWorldPublicPrefabIds.CabinDoor,
            GamePlayableWorld.DoorPosition, WorldYaw.QuarterTurn,
            revision: 2);
        var delta = new WorldPublicDeltaRecord(
            WorldPublicOperation.Present,
            WorldPublicFieldMask.Transform,
            openDoor.Id,
            openDoor.EntityPublicVersion,
            openDoor);
        var batch = new ChunkPublicationBatch(
            ChunkPublicationReadStatus.Delta,
            doorKey,
            new ChunkPublicationCursor(epoch, doorKey, new ChunkPublicationRevision(2)),
            new ChunkPublicationRevision(1),
            1,
            1);
        Expect(cache.TryApplyDelta(doorHandle, batch, [delta], out _),
            "The native door semantic update did not apply.");
        GodotWorldRendererSnapshot beforeRefresh = renderer.GetSnapshot();
        Expect(renderer.TryQueuePublicRefresh(doorHandle, out _),
            "The native door public refresh did not queue.");
        renderer.ProcessFrame(new GodotWorldFrameBudget(1, 0));
        GodotWorldRendererSnapshot afterRefresh = renderer.GetSnapshot();
        Expect(afterRefresh.PublicRefreshes == beforeRefresh.PublicRefreshes + 1 &&
            afterRefresh.CompletedBuilds == beforeRefresh.CompletedBuilds,
            "Opening the door rebuilt immutable cabin presentation.");

        Expect(window.TryTrackPlayer(
            crossed, changes, out ClientPresentationWindowTransition sameCenter,
            out _, out _, out _),
            "Same-center native movement failed.");
        Expect(sameCenter.Entered == 0 && sameCenter.Exited == 0,
            "Same-center movement changed native membership.");

        double peakResources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        double peakBuffers = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        renderer.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        double resourcesAfter = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        double buffersAfter = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        Expect(resourcesAfter <= peakResources && buffersAfter <= peakBuffers,
            "Native resource pressure continued growing after playable renderer disposal.");

        GD.Print(
            $"T0.10 METRICS pack_bytes={pack.Length} identity_index_bytes={bound!.StructuralIdentityIndexReservedBytes} " +
            $"semantic_reserved_bytes={cache.GetSnapshot().ReservedBytes} active_chunks={afterRefresh.ActiveRenderChunks} " +
            $"active_nodes={afterRefresh.ActiveNodes} active_resources={afterRefresh.ActiveResources} " +
            $"builds={afterRefresh.CompletedBuilds} public_refreshes={afterRefresh.PublicRefreshes} " +
            $"crossing_frames={frames} frame_max_us={afterRefresh.MaximumFrameMicroseconds:F3} " +
            $"mesh_max_us={afterRefresh.MaximumMeshBuildMicroseconds:F3} " +
            $"resource_baseline={resourceBaseline:F0} resource_peak={peakResources:F0} " +
            $"resource_after={resourcesAfter:F0} buffer_baseline={bufferBaseline:F0} " +
            $"buffer_peak={peakBuffers:F0} buffer_after={buffersAfter:F0}");
    }

    private static void StageWindow(
        ClientWorldSemanticCache cache,
        PublicationEpoch epoch,
        SpatialChunkCoord center)
    {
        for (int z = center.Z - 1; z <= center.Z + 1; z++)
        {
            for (int x = center.X - 1; x <= center.X + 1; x++)
                StageIfMissing(cache, epoch, Key(x, z));
        }
    }

    private static void StageIfMissing(
        ClientWorldSemanticCache cache,
        PublicationEpoch epoch,
        SpatialChunkKey key)
    {
        if (cache.TryGetResidentHandle(key, out _)) return;
        Expect(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _),
            $"Native semantic request failed for {key}.");
        WorldPublicRecord[] rows = PublicRows(key);
        ChunkPublicationCursor cursor = rows.Length == 0
            ? default
            : new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(1));
        Expect(cache.TryStageFromBaseWorld(handle, rows, cursor, out _) &&
            cache.TryPublish(handle, out _),
            $"Native semantic stage/publish failed for {key}.");
    }

    private static WorldPublicRecord[] PublicRows(SpatialChunkKey key)
    {
        if (key.Coord == new SpatialChunkCoord(0, 0))
        {
            return
            [
                Public(1, 1, GameWorldPublicPrefabIds.Character,
                    GamePlayableWorld.PlayerStart, WorldYaw.Zero, 1),
                Public(4, 1, GameWorldPublicPrefabIds.WoodlandAnimal,
                    GamePlayableWorld.AnimalPosition, WorldYaw.Zero, 1),
            ];
        }
        if (key.Coord == new SpatialChunkCoord(1, 0))
        {
            return
            [
                Public(2, 1, GameWorldPublicPrefabIds.CabinDoor,
                    GamePlayableWorld.DoorPosition, WorldYaw.Zero, 1),
                Public(3, 1, GameWorldPublicPrefabIds.WorldChest,
                    GamePlayableWorld.ChestPosition, WorldYaw.Zero, 1),
            ];
        }
        return [];
    }

    private static WorldPublicRecord Public(
        uint index,
        ulong version,
        uint prefab,
        WorldPosition position,
        WorldYaw orientation,
        ulong revision) => new(
            new WorldPublicEntityId(index, 1),
            version,
            prefab,
            position,
            orientation,
            WorldPublicStateFlags.Visible,
            new ChunkPublicationRevision(revision));

    private static void ValidateGodotTerrainFrontFaces(Node root)
    {
        MeshInstance3D? terrain = root.FindChild("BaseTerrain", recursive: true, owned: false)
            as MeshInstance3D;
        ArrayMesh? mesh = terrain?.Mesh as ArrayMesh;
        Expect(mesh is not null && mesh.GetSurfaceCount() != 0,
            "The native terrain mesh could not be inspected.");
        if (mesh is null) return;
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            using var data = new MeshDataTool();
            Expect(data.CreateFromSurface(mesh, surface) == Error.Ok && data.GetFaceCount() != 0,
                "A native terrain surface had no inspectable triangles.");
            for (int face = 0; face < data.GetFaceCount(); face++)
            {
                Vector3 first = data.GetVertex(data.GetFaceVertex(face, 0));
                Vector3 second = data.GetVertex(data.GetFaceVertex(face, 1));
                Vector3 third = data.GetVertex(data.GetFaceVertex(face, 2));
                Expect((second - first).Cross(third - first).Y < 0,
                    "A Godot terrain triangle was not clockwise when viewed from above.");
            }
        }
    }

    private static void ValidateGodotGeometryConventions()
    {
        GodotMeshGeometry.ConvertCanonicalTriangle(0, 1, 2,
            out int first, out int second, out int third);
        Expect(first == 0 && second == 2 && third == 1,
            "The Godot adapter did not own canonical triangle conversion.");

        var material = new StandardMaterial3D();
        ArrayMesh mesh = GodotMeshGeometry.CreateGroundedBox(new Vector3(2f, 3f, 4f), material);
        using var data = new MeshDataTool();
        Expect(data.CreateFromSurface(mesh, 0) == Error.Ok && data.GetFaceCount() == 12,
            "The Godot procedural box did not expose twelve triangles.");
        for (int face = 0; face < data.GetFaceCount(); face++)
        {
            int firstVertex = data.GetFaceVertex(face, 0);
            int secondVertex = data.GetFaceVertex(face, 1);
            int thirdVertex = data.GetFaceVertex(face, 2);
            Vector3 a = data.GetVertex(firstVertex);
            Vector3 b = data.GetVertex(secondVertex);
            Vector3 c = data.GetVertex(thirdVertex);
            Vector3 triangleCenter = (a + b + c) / 3f;
            Vector3 outward = ExpectedBoxNormal(triangleCenter);
            Vector3 storedNormal = (data.GetVertexNormal(firstVertex) +
                data.GetVertexNormal(secondVertex) +
                data.GetVertexNormal(thirdVertex)).Normalized();
            float normalDot = storedNormal.Dot(outward);
            float windingDot = (b - a).Cross(c - a).Normalized().Dot(outward);
            Expect(normalDot > 0.999f,
                $"Godot procedural box face {face} normal was not outward: " +
                $"dot={normalDot}, center={triangleCenter}, normal={storedNormal}.");
            Expect(windingDot < -0.999f,
                $"Godot procedural box face {face} was not native clockwise winding: " +
                $"dot={windingDot}.");
        }

        static Vector3 ExpectedBoxNormal(Vector3 faceCenter)
        {
            if (Mathf.IsEqualApprox(faceCenter.Y, 0f)) return Vector3.Down;
            if (Mathf.IsEqualApprox(faceCenter.Y, 3f)) return Vector3.Up;
            if (Mathf.IsEqualApprox(faceCenter.X, -1f)) return Vector3.Left;
            if (Mathf.IsEqualApprox(faceCenter.X, 1f)) return Vector3.Right;
            if (Mathf.IsEqualApprox(faceCenter.Z, -2f)) return Vector3.Forward;
            if (Mathf.IsEqualApprox(faceCenter.Z, 2f)) return Vector3.Back;
            throw new InvalidOperationException($"A procedural box face was not planar: {faceCenter}.");
        }

        ArrayMesh wedge = GodotMeshGeometry.CreateGroundedPointerWedge(
            0.42f, 0.42f, 0.68f, 1.8f, material);
        using var wedgeData = new MeshDataTool();
        Expect(wedgeData.CreateFromSurface(wedge, 0) == Error.Ok &&
            wedgeData.GetFaceCount() == 8,
            "The Godot character wedge did not expose eight triangles.");
        Vector3 inside = new(0f, 0.9f, -0.05f);
        for (int face = 0; face < wedgeData.GetFaceCount(); face++)
        {
            int firstVertex = wedgeData.GetFaceVertex(face, 0);
            int secondVertex = wedgeData.GetFaceVertex(face, 1);
            int thirdVertex = wedgeData.GetFaceVertex(face, 2);
            Vector3 a = wedgeData.GetVertex(firstVertex);
            Vector3 b = wedgeData.GetVertex(secondVertex);
            Vector3 c = wedgeData.GetVertex(thirdVertex);
            Vector3 normal = (wedgeData.GetVertexNormal(firstVertex) +
                wedgeData.GetVertexNormal(secondVertex) +
                wedgeData.GetVertexNormal(thirdVertex)).Normalized();
            Vector3 rawCross = (b - a).Cross(c - a).Normalized();
            Expect(normal.Dot((a + b + c) / 3f - inside) > 0f,
                $"Godot character wedge face {face} normal pointed into the prism.");
            Expect(rawCross.Dot(normal) < -0.999f,
                $"Godot character wedge face {face} did not preserve native winding plus outward normal.");
        }
    }

    private static int Drain(GodotWorldRenderer renderer)
    {
        int frames = 0;
        while (renderer.GetSnapshot().PendingBuilds != 0 ||
            renderer.GetSnapshot().PendingRetirements != 0)
        {
            renderer.ProcessFrame(new GodotWorldFrameBudget(2, 2));
            if (++frames > 64) throw new InvalidOperationException("Native playable work did not converge.");
        }
        return frames;
    }

    private static SpatialChunkKey Key(int x, int z) =>
        new(GameWorldState.DefaultSpace, x, z);

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
