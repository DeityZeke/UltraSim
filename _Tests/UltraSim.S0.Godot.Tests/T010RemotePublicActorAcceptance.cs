using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Persistence;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Server.Persistence;
using UltraSim.Framework.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;

public partial class T010PlayableWorldAcceptance
{
    private static readonly ChunkInterestProfile s_remoteActorInterest = new(
        ImmediateEnterRadiusMillimeters: 1_000,
        ImmediateLeaveRadiusMillimeters: 1_500,
        NearEnterRadiusMillimeters: 2_000,
        NearLeaveRadiusMillimeters: 2_500,
        FarEnterRadiusMillimeters: 3_500,
        FarLeaveRadiusMillimeters: 3_900);

    /// <summary>
    /// Real authority-to-presentation composition. Earlier T0.10 coverage consumed the
    /// publication store directly, while N1/H0 coverage used synthetic headless sources. This
    /// fixture deliberately joins those accepted seams without creating another adapter.
    /// </summary>
    private void RunRemotePublicActorAcceptance()
    {
        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        BoundWorldPack bound = BindPlayablePack(pack);
        var composition = new UltraSimGameComposition(
            new UltraSim.Game.Inventory.InventoryLimits(32, 128, 32),
            baseWorldPack: pack);
        var options = new ServerRuntimeOptions(
            UltraSimGameComposition.CreateWorldOptions(
                128,
                new PersistenceIdentityOptions(new PersistentWorldId(0x80_3011))),
            WorldClockOptions.Default);
        using var runtime = new ServerRuntime(composition, options);
        runtime.Initialize();
        runtime.Start();
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(new ParticipantId(3011));
        Expect(entered.IsEntered, "The remote public-actor participant could not enter authority.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
            "The remote public-actor authority could not populate.");
        runtime.Tick();

        ChunkPublicationStore source = GetPublicationSource(runtime);
        var cache = new ClientWorldSemanticCache(
            bound,
            new ClientWorldSemanticCacheLimits(8, 16, 16, 16, 64));
        using var renderer = new GodotWorldRenderer(
            this,
            cache,
            GodotWorldRendererLimits.ForStructuralWindow(0, 1, 16));
        var window = new GodotSlidingRenderWindow(cache, renderer, 0);
        var presentationChanges = new ClientPresentationChange[2];

        using var link = new RemoteActorLink(source, cache);
        link.Reconcile(GamePlayableWorld.AnimalPosition);
        TrackRemoteActorWindow(window, renderer, cache, GamePlayableWorld.AnimalPosition,
            presentationChanges, reset: false);

        SpatialChunkKey animalKey = GamePlayableWorld.AnimalPosition.ToChunkKey(GameWorldState.DefaultSpace);
        WorldPublicRecord initialAnimal = FindPublicRecord(
            cache, animalKey, GameWorldPublicPrefabIds.WoodlandAnimal);
        var temporal = new ClientRemoteActorInterpolation();
        Expect(temporal.Observe(source.Epoch.Value, initialAnimal.Id,
                initialAnimal.EntityPublicVersion,
                ClientMotionPose.FromAuthority(initialAnimal.Position,
                    initialAnimal.Orientation), 1_000_000),
            "The first remote actor pose could not enter temporal presentation.");
        Expect(CountPresentedPrefab(renderer.Root, GameWorldPublicPrefabIds.WoodlandAnimal) == 1,
            "The remotely replicated authoritative animal was not presented by Godot.");
        GodotWorldRenderChunkDiagnostics initialDiagnostics = default;
        Expect(cache.TryGetResidentHandle(animalKey, out ClientSemanticChunkHandle initialHandle) &&
            renderer.TryGetChunkDiagnostics(initialHandle, out initialDiagnostics),
            "The initial remote actor render diagnostics were unavailable.");

        string saveRoot = Path.Combine(
            Path.GetTempPath(), $"ultrasim-t010-remote-actor-{Guid.NewGuid():N}");
        try
        {
            var persistence = new GameRuntimePersistence(
                runtime.Game,
                composition,
                RemoteActorRestoreOptions(),
                new SaveSetCoordinator(saveRoot));

            WorldPosition movedAnimal = new(
                GamePlayableWorld.AnimalPosition.Chunk.X,
                GamePlayableWorld.AnimalPosition.Chunk.Z,
                checked((ushort)(GamePlayableWorld.AnimalPosition.Local.XMillimeters + 1_000)),
                GamePlayableWorld.AnimalPosition.Local.ZMillimeters,
                GamePlayableWorld.AnimalPosition.YMillimeters);
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (world, _, game) => game.WorldState.TrySetPosition(
                    world, ResolveRemoteActor(world, entities.WoodlandAnimalId), movedAnimal),
                out bool moved) && moved,
                "The authoritative woodland actor could not move.");
            runtime.Tick();
            link.ObserveCommitAndConverge(source);
            RefreshPresentedChunk(renderer, cache, animalKey);

            WorldPublicRecord movedRecord = FindPublicRecord(
                cache, animalKey, GameWorldPublicPrefabIds.WoodlandAnimal);
            Expect(renderer.TryGetChunkDiagnostics(
                    initialHandle, out GodotWorldRenderChunkDiagnostics movedDiagnostics),
                "The moved remote actor render diagnostics were unavailable.");
            Expect(movedRecord.Id == initialAnimal.Id &&
                movedRecord.EntityPublicVersion > initialAnimal.EntityPublicVersion &&
                movedRecord.Position == movedAnimal &&
                movedDiagnostics.PublicObjectId != initialDiagnostics.PublicObjectId,
                "The authoritative movement did not update the remote semantic and Godot actor: " +
                $"id={initialAnimal.Id}->{movedRecord.Id}, " +
                $"version={initialAnimal.EntityPublicVersion}->{movedRecord.EntityPublicVersion}, " +
                $"position={initialAnimal.Position}->{movedRecord.Position} expected={movedAnimal}, " +
                $"public_root={initialDiagnostics.PublicObjectId}->{movedDiagnostics.PublicObjectId}.");
            Expect(temporal.Observe(source.Epoch.Value, movedRecord.Id,
                    movedRecord.EntityPublicVersion,
                    ClientMotionPose.FromAuthority(movedRecord.Position,
                        movedRecord.Orientation), 1_100_000) &&
                temporal.TrySample(movedRecord.Id, 1_150_000,
                    out ClientResolvedMotionPose intermediate) &&
                intermediate.Provenance == ClientMotionPoseProvenance.InterpolatedRemote &&
                intermediate.Pose.GlobalXMillimeters > initialAnimal.Position.GlobalXMillimeters &&
                intermediate.Pose.GlobalXMillimeters < movedRecord.Position.GlobalXMillimeters,
                "The real replicated actor did not produce a bounded intermediate pose.");

            GameSaveResult saved = persistence.TrySave(
                new SaveSetContext(1, 1_700_003_011_001), static capture => capture());
            Expect(saved.Succeeded, "The public-actor generation checkpoint could not save.");

            WorldPosition away = new(2, 2, 8_000, 8_000, 0);
            link.Reconcile(away);
            TrackRemoteActorWindow(window, renderer, cache, away, presentationChanges, reset: true);
            Expect(CountPresentedPrefab(renderer.Root, GameWorldPublicPrefabIds.WoodlandAnimal) == 0 &&
                !cache.TryGetResidentHandle(animalKey, out _),
                "Leaving interest retained the old public actor or semantic chunk.");

            link.Reconcile(movedAnimal);
            TrackRemoteActorWindow(window, renderer, cache, movedAnimal, presentationChanges, reset: true);
            bool reentered = TryFindPublicRecord(
                cache, animalKey, GameWorldPublicPrefabIds.WoodlandAnimal, out WorldPublicRecord reenteredRecord);
            int reenteredPresented = CountPresentedPrefab(
                renderer.Root, GameWorldPublicPrefabIds.WoodlandAnimal);
            GodotWorldRendererSnapshot reenteredRender = renderer.GetSnapshot();
            ClientPresentationWindowSnapshot reenteredWindow = window.GetSnapshot();
            Expect(reentered && reenteredRecord.Position == movedAnimal && reenteredPresented == 1,
                "Re-entry did not recover the current authoritative actor: " +
                $"semantic={reentered}, position={reenteredRecord.Position}, " +
                $"expected={movedAnimal}, presented={reenteredPresented}, " +
                $"rendered={reenteredRender.ActiveRenderChunks}/{reenteredRender.VisibleRenderChunks}, " +
                $"builds={reenteredRender.CompletedBuilds}, retires={reenteredRender.CompletedRetirements}, " +
                $"window={reenteredWindow.ActiveChunks}.");

            link.Reconnect(source, movedAnimal);
            RefreshPresentedChunk(renderer, cache, animalKey);
            Expect(FindPublicRecord(cache, animalKey, GameWorldPublicPrefabIds.WoodlandAnimal).Position ==
                movedAnimal && link.CompletedReconnects == 1,
                "A fresh remote session did not resynchronize current actor truth.");

            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (world, _, _) => world.TryDestroy(
                    ResolveRemoteActor(world, entities.WoodlandAnimalId)),
                out bool destroyed) && destroyed,
                "The authoritative woodland actor could not be destroyed.");
            runtime.Tick();
            Expect(!SourceContainsPrefab(
                    source, animalKey, GameWorldPublicPrefabIds.WoodlandAnimal),
                "The authoritative publication source retained the destroyed woodland actor.");
            link.ObserveCommitAndConverge(source);
            RefreshPresentedChunk(renderer, cache, animalKey);
            bool destroyedSemantic = !TryFindPublicRecord(
                cache, animalKey, GameWorldPublicPrefabIds.WoodlandAnimal, out _);
            int destroyedPresented = CountPresentedPrefab(
                renderer.Root, GameWorldPublicPrefabIds.WoodlandAnimal);
            cache.TryGetResidentHandle(animalKey, out ClientSemanticChunkHandle destroyedHandle);
            cache.TryGetInfo(destroyedHandle, out ClientSemanticChunkInfo destroyedInfo);
            RemoteChunkWorldStateServerSnapshot destroyedServer = link.ServerSnapshot;
            RemoteChunkWorldStateClientSnapshot destroyedClient = link.ClientSnapshot;
            Expect(destroyedSemantic && destroyedPresented == 0,
                "Authoritative destruction did not remove the remote Godot actor: " +
                $"semantic_removed={destroyedSemantic}, presented={destroyedPresented}, " +
                $"cursor={destroyedInfo.PublicationCursor.Revision.Value}, " +
                $"queued_deltas={destroyedServer.DeltasQueued}, " +
                $"applied_deltas={destroyedClient.AppliedDeltas}, " +
                $"rejected={destroyedClient.RejectedDeliveries}, " +
                $"contradictory={destroyedClient.ContradictoryDeliveries}, " +
                $"lineage={link.Describe(animalKey)}.");
            temporal.Remove(initialAnimal.Id);
            Expect(!temporal.TrySample(initialAnimal.Id, 1_200_000, out _),
                "The destroyed public actor retained temporal samples.");

            GameLoadResult loaded = persistence.TryLoad();
            Expect(loaded.Succeeded,
                "The saved authority could not publish a replacement server generation.");
            ChunkPublicationStore replacement = GetPublicationSource(runtime);
            Expect(replacement.Epoch != source.Epoch,
                "Generation replacement retained the old publication epoch.");
            link.ReplaceSourceAndConverge(replacement, movedAnimal);
            RefreshPresentedChunk(renderer, cache, animalKey);
            WorldPublicRecord restored = FindPublicRecord(
                cache, animalKey, GameWorldPublicPrefabIds.WoodlandAnimal);
            temporal.ResetGeneration(replacement.Epoch.Value);
            Expect(!temporal.TrySample(initialAnimal.Id, 1_300_000, out _) &&
                temporal.Observe(replacement.Epoch.Value, restored.Id,
                    restored.EntityPublicVersion,
                    ClientMotionPose.FromAuthority(restored.Position,
                        restored.Orientation), 1_300_000) &&
                temporal.TrySample(restored.Id, 1_300_000,
                    out ClientResolvedMotionPose restoredPose) &&
                restoredPose.Pose == ClientMotionPose.FromAuthority(
                    restored.Position, restored.Orientation),
                "A replacement source inherited old-generation temporal samples.");
            Expect(restored.Position == movedAnimal &&
                cache.GetSnapshot().PublicationEpoch == replacement.Epoch &&
                CountPresentedPrefab(renderer.Root, GameWorldPublicPrefabIds.WoodlandAnimal) == 1,
                "The replacement generation did not fence old truth and restore the current actor.");

            RemoteChunkWorldStateServerSnapshot server = link.ServerSnapshot;
            RemoteChunkWorldStateClientSnapshot client = link.ClientSnapshot;
            GD.Print(
                $"T0.10 REMOTE ACTOR METRICS reconnects={link.CompletedReconnects} " +
                $"source_replacements={link.CompletedSourceReplacements} " +
                $"snapshots={client.AppliedSnapshots} deltas={client.AppliedDeltas} " +
                $"proofs={server.AppliedProofs} public_refreshes={renderer.GetSnapshot().PublicRefreshes} " +
                $"semantic_bytes={cache.GetSnapshot().ReservedBytes} " +
                $"server_transport_bytes={server.ReservedBytes} client_transport_bytes={client.ReservedBytes}");
        }
        finally
        {
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, recursive: true);
        }
    }

    private static ChunkPublicationStore GetPublicationSource(ServerRuntime runtime)
    {
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, ChunkPublicationStore>(
            (_, _, game) => game.WorldState.Publications,
            out ChunkPublicationStore? source),
            "The active publication source was unavailable.");
        return source!;
    }

    private static Entity ResolveRemoteActor(EcsWorld world, PersistentEntityId id) =>
        world.TryResolvePersistentEntity(id, out Entity entity)
            ? entity
            : throw new InvalidOperationException($"Remote public actor {id.Value} did not resolve.");

    private static PersistenceRestoreOptions RemoteActorRestoreOptions() => new(
        new PersistenceLimits(
            maximumArtifactBytes: 16 * 1024 * 1024,
            maximumEntities: 100_000,
            maximumShapes: 64,
            maximumComponents: 64,
            maximumSections: 256,
            maximumComponentPayloadBytes: 8 * 1024 * 1024,
            maximumTemporaryBytes: 64 * 1024 * 1024),
        ChunkLayoutOptions.KiB16);

    private static void TrackRemoteActorWindow(
        GodotSlidingRenderWindow window,
        GodotWorldRenderer renderer,
        ClientWorldSemanticCache cache,
        in WorldPosition position,
        Span<ClientPresentationChange> changes,
        bool reset)
    {
        if (reset)
        {
            Expect(window.TryResetForJump(position, changes, out _, out int required,
                    out ClientPresentationWindowError resetError,
                    out GodotWorldRenderQueueError resetRendererError) &&
                resetError == ClientPresentationWindowError.None &&
                resetRendererError == GodotWorldRenderQueueError.None &&
                RetireExitedAfterRender(renderer, cache, changes, required),
                "The remote actor presentation window could not retire its prior jump origin.");
        }
        Expect(window.TryTrackPlayer(position, changes, out _, out _,
                out ClientPresentationWindowError error,
                out GodotWorldRenderQueueError rendererError) &&
            error == ClientPresentationWindowError.None &&
            rendererError == GodotWorldRenderQueueError.None,
            "The remote actor presentation window rejected an accepted interest change.");
        _ = Drain(renderer);
    }

    private static bool RetireExitedAfterRender(
        GodotWorldRenderer renderer,
        ClientWorldSemanticCache cache,
        Span<ClientPresentationChange> changes,
        int required)
    {
        _ = Drain(renderer);
        for (int index = 0; index < required; index++)
        {
            if (changes[index].Kind != ClientPresentationChangeKind.Exit) continue;
            if (!cache.BeginRetire(changes[index].Handle) ||
                !cache.CompleteRetire(changes[index].Handle))
                return false;
        }
        return true;
    }

    private static void RefreshPresentedChunk(
        GodotWorldRenderer renderer,
        ClientWorldSemanticCache cache,
        SpatialChunkKey key)
    {
        Expect(cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) &&
            renderer.TryQueuePublicRefresh(handle, out GodotWorldRenderQueueError error) &&
            error == GodotWorldRenderQueueError.None,
            $"The remote public refresh for {key} did not queue.");
        _ = Drain(renderer);
    }

    private static bool TryFindPublicRecord(
        ClientWorldSemanticCache cache,
        SpatialChunkKey key,
        uint prefabId,
        out WorldPublicRecord record)
    {
        if (cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) &&
            cache.TryGetResidentView(handle, out ClientSemanticChunkView view))
        {
            for (int index = 0; index < view.PublicRecords.Length; index++)
            {
                if (view.PublicRecords[index].PrefabId != prefabId) continue;
                record = view.PublicRecords[index];
                return true;
            }
        }
        record = default;
        return false;
    }

    private static WorldPublicRecord FindPublicRecord(
        ClientWorldSemanticCache cache,
        SpatialChunkKey key,
        uint prefabId)
    {
        Expect(TryFindPublicRecord(cache, key, prefabId, out WorldPublicRecord record),
            $"Public prefab {prefabId} was absent from semantic chunk {key}.");
        return record;
    }

    private static bool SourceContainsPrefab(
        ChunkPublicationStore source,
        SpatialChunkKey key,
        uint prefabId)
    {
        Span<WorldPublicRecord> rows = stackalloc WorldPublicRecord[16];
        ChunkPublicationReadStatus status = source.TryCopySnapshot(
            key, rows, out ChunkPublicationBatch batch);
        if (status == ChunkPublicationReadStatus.ChunkAbsent) return false;
        Expect(status == ChunkPublicationReadStatus.Snapshot,
            $"The authoritative source could not expose current chunk {key}: {status}.");
        for (int index = 0; index < batch.Count; index++)
            if (rows[index].PrefabId == prefabId) return true;
        return false;
    }

    private static int CountPresentedPrefab(Node root, uint prefabId)
    {
        string expectedName = $"Public_{prefabId}";
        int count = 0;
        Count(root);
        return count;

        void Count(Node node)
        {
            if (node is MultiMeshInstance3D instance && instance.Name == expectedName &&
                instance.Multimesh is not null)
                count += instance.Multimesh.InstanceCount;
            foreach (Node child in node.GetChildren()) Count(child);
        }
    }

    private sealed class RemoteActorLink : IDisposable
    {
        private readonly ClientWorldSemanticCache _cache;
        private readonly OfflineDirectChunkSubscriptionChange[] _changes =
            new OfflineDirectChunkSubscriptionChange[8];
        private ProtocolLoopback _loopback = null!;
        private OfflineDirectChunkRecipient _recipient = null!;
        private ChunkPublicationWorkScheduler _scheduler = null!;
        private ChunkReplicationRecipientId _recipientId;
        private RemoteChunkWorldStateServer _server = null!;
        private RemoteChunkWorldStateClient _client = null!;
        private ChunkPublicationStore _source;
        private ulong _cadenceStep;
        private ulong _sessionEpoch = 30_110;

        internal RemoteActorLink(ChunkPublicationStore source, ClientWorldSemanticCache cache)
        {
            _source = source;
            _cache = cache;
            BuildConnection();
        }

        internal int CompletedReconnects { get; private set; }
        internal int CompletedSourceReplacements { get; private set; }
        internal RemoteChunkWorldStateServerSnapshot ServerSnapshot => _server.GetSnapshot();
        internal RemoteChunkWorldStateClientSnapshot ClientSnapshot => _client.GetSnapshot();

        internal string Describe(SpatialChunkKey key)
        {
            ulong applied = 0;
            bool knowledge = false;
            for (int index = 0; index < _recipient.Subscriptions.Length; index++)
            {
                OfflineDirectChunkSubscription subscription = _recipient.Subscriptions[index];
                if (subscription.Interest.Key != key) continue;
                applied = subscription.Cursor.IsValid ? subscription.Cursor.Revision.Value : 0;
                knowledge = subscription.HasKnowledge;
                break;
            }
            Span<WorldPublicRecord> rows = stackalloc WorldPublicRecord[16];
            ChunkPublicationReadStatus status = _source.TryCopySnapshot(
                key, rows, out ChunkPublicationBatch batch);
            return $"recipient={applied}/{knowledge},source={status}/" +
                $"{(batch.Cursor.IsValid ? batch.Cursor.Revision.Value : 0)}/floor={batch.HistoryFloor.Value}";
        }

        internal void Reconcile(in WorldPosition anchor)
        {
            Expect(_server.Reconcile(GameWorldState.DefaultSpace, anchor, _changes,
                    out OfflineDirectChunkReconcileResult result) ==
                OfflineDirectChunkReconcileStatus.Reconciled,
                "Remote actor interest reconciliation failed.");
            if (result.SourceReset) _cache.ResetPublicationEpoch(result.SourceEpoch);
            for (int index = 0; index < result.ChangeCount; index++)
            {
                ref readonly OfflineDirectChunkSubscriptionChange change = ref _changes[index];
                if (change.Kind != OfflineDirectChunkSubscriptionChangeKind.Entered ||
                    _cache.TryGetResidentHandle(change.Key, out _))
                    continue;
                Expect(_cache.TryRequest(change.Key, out ClientSemanticChunkHandle handle, out _) &&
                    _cache.TryStageFromBaseWorld(handle, [], default, out _) &&
                    _cache.TryPublish(handle, out _),
                    $"Remote actor base chunk {change.Key} could not become resident.");
            }
            Expect(_scheduler.TryApplyReconciliation(
                    _recipientId, _changes.AsSpan(0, result.ChangeCount), result),
                "The selected chunk scheduler rejected accepted actor interest.");
            ScheduleAndConverge();
        }

        internal void ObserveCommitAndConverge(ChunkPublicationStore source)
        {
            Expect(ReferenceEquals(source, _source),
                "The remote actor link observed an unbound publication source.");
            ChunkPublicationCommitObservationStatus observed = _scheduler.ObserveCommit(source);
            Expect(observed == ChunkPublicationCommitObservationStatus.Observed,
                $"The selected scheduler did not observe the actor commit: {observed}.");
            ScheduleAndConverge();
        }

        internal void Reconnect(ChunkPublicationStore source, in WorldPosition anchor)
        {
            _loopback.Dispose();
            _source = source;
            _sessionEpoch++;
            BuildConnection();
            Reconcile(anchor);
            CompletedReconnects++;
        }

        internal void ReplaceSourceAndConverge(
            ChunkPublicationStore replacement,
            in WorldPosition anchor)
        {
            _server.ReplaceSource(replacement);
            _scheduler.ReplaceSource(replacement);
            _source = replacement;
            Reconcile(anchor);
            CompletedSourceReplacements++;
        }

        public void Dispose() => _loopback.Dispose();

        private void BuildConnection()
        {
            Expect(ProtocolLoopback.TryCreate(
                    NetworkProtocolLimits.Default,
                    new DeterministicDatagramLimits(2_048, 1_472),
                    default,
                    out ProtocolLoopback? loopback,
                    out _,
                    UltraSimGameProtocol.Manifest),
                "The remote actor secure protocol loopback could not establish.");
            _loopback = loopback!;
            _recipient = new OfflineDirectChunkRecipient(
                new OfflineDirectChunkRecipientLimits(8, 16, 16),
                s_remoteActorInterest,
                new OfflineDirectChunkCadence(1, 1, 1));
            _scheduler = new ChunkPublicationWorkScheduler(
                _source,
                new ChunkPublicationWorkSchedulerLimits(1, 8));
            Expect(_scheduler.TryRegister(_recipient, out _recipientId),
                "The selected chunk scheduler could not register the remote actor recipient.");
            _server = new RemoteChunkWorldStateServer(
                _loopback.Server,
                _recipient,
                _source,
                _sessionEpoch,
                new RemoteChunkWorldStateTransportLimits(16));
            _client = new RemoteChunkWorldStateClient(
                _loopback.Client,
                _cache,
                _sessionEpoch,
                maximumDeltaRecords: 16,
                maximumPendingProofs: 16);
        }

        private void ScheduleAndConverge()
        {
            while (_scheduler.TryDequeueDue(_cadenceStep, out ChunkPublicationWork work))
            {
                Expect(work.Recipient == _recipientId,
                    "The one-recipient scheduler produced foreign actor work.");
                RemoteChunkWorldStateScheduleStatus status =
                    _server.TrySchedule(work.Key, _cadenceStep);
                Expect(status is RemoteChunkWorldStateScheduleStatus.QueuedDelta or
                    RemoteChunkWorldStateScheduleStatus.QueuedAbsence or
                    RemoteChunkWorldStateScheduleStatus.NoPayload or
                    RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted,
                    $"Remote actor work could not schedule: {status}.");
            }

            for (int attempt = 0; attempt < 4_096; attempt++)
            {
                Pump();
                RemoteChunkWorldStateServerSnapshot server = _server.GetSnapshot();
                RemoteChunkWorldStateClientSnapshot client = _client.GetSnapshot();
                if (server.OutstandingDeliveries == 0 && !server.SnapshotTransferActive &&
                    !server.SnapshotCancelPending && client.PendingProofs == 0 &&
                    !client.SnapshotCandidateActive)
                {
                    _cadenceStep++;
                    return;
                }
            }
            throw new InvalidOperationException("Remote actor delivery/proof did not converge.");
        }

        private void Pump()
        {
            _ = _server.TryProgress();
            _loopback.Pump();
            while (_loopback.Client.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                Expect(_client.TryProcess(message, payload),
                    $"The remote actor client rejected Network message {message.MessageId.Value}.");
                Expect(_loopback.Client.TryConsumeReceived(),
                    "The remote actor client could not consume a handled Network message.");
            }
            _ = _client.TryProgress();
            while (_loopback.Server.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                Expect(_server.TryProcess(message, payload),
                    $"The remote actor server rejected Network message {message.MessageId.Value}.");
                Expect(_loopback.Server.TryConsumeReceived(),
                    "The remote actor server could not consume a handled Network message.");
            }
        }
    }
}
