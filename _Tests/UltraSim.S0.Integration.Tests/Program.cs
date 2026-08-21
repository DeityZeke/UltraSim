using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UltraSim.Client.Core;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Inventory;
using UltraSim.Game.Input;
using UltraSim.Game.Persistence;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Server.Hosting;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;
using UltraSim.UI;

const string Workload = "ultrasim-t0.10-integrated-acceptance-v1";
string? output = ReadOption(args, "--output");

var results = new List<TestResult>();
Run("PlayableWoodsCabinOfflineDirectRoundTrip", PlayableWoodsCabinRoundTrip, results);
Run("AccountBackedInternalToHeadlessCanonicalRoundTrip", AccountBackedRoundTrip, results);
Run("AccountlessAdmissionAndPersistence", AccountlessRoundTrip, results);
Run("AuthoritativeClockRoundTripsAcrossHostCadences", AuthoritativeClockRoundTripsAcrossHostCadences, results);
Run("WarmedRuntimeTickHasNoAllocationAndBoundedWrapperTax", RuntimeTickBenchmark, results);

var report = new AcceptanceResult(
    Workload,
    DateTimeOffset.UtcNow,
    Environment.Version.ToString(),
    Environment.ProcessorCount,
    results.ToArray());
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (!string.IsNullOrWhiteSpace(output))
{
    string path = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json + Environment.NewLine);
}
Console.WriteLine($"All {results.Count} integrated acceptance gates passed.");

static TestMetrics PlayableWoodsCabinRoundTrip()
{
    string root = TempRoot("t0-10-playable");
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_0010));
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    try
    {
        using var host = new InternalHost(composition, RuntimeOptions(identity));
        host.Start();
        ServerRuntime runtime = host.RuntimeForTesting;
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(new ParticipantId(101));
        Expect(entered.IsEntered, "T0.10 accountless entry failed.");

        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
            "T0.10 authored dynamic fixture could not be populated.");
        host.Tick();

        WorldPosition playerPosition = ReadWorldPosition(runtime.Game, entities.PlayerId);
        PlayableClientProjection client = BuildPlayableProjection(runtime.Game, playerPosition);
        Expect(client.Window.GetSnapshot().ActiveChunks == 9,
            "The initial presentation window was not bounded to nine chunks.");
        string initialProjection = CaptureProjectionCanonical(client);
        Expect(initialProjection.Length != 0, "The initial client projection was empty.");

        var input = new ClientWorldInputSequencer();
        Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.Interact, true), out GamePlayerCommand openDoor),
            "The door input command could not be formed.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, generationId, game) => game.WorldActions.ToggleCabinDoor(
                world, game.WorldState,
                ResolveInWorld(world, entities.PlayerId),
                ResolveInWorld(world, entities.CabinDoorId),
                openDoor,
                out _),
            out GameWorldActionStatus doorStatus) && doorStatus == GameWorldActionStatus.Accepted,
            "The authoritative cabin door did not open.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, generationId, game) => game.WorldActions.ToggleCabinDoor(
                world, game.WorldState,
                ResolveInWorld(world, entities.PlayerId),
                ResolveInWorld(world, entities.CabinDoorId),
                openDoor,
                out _),
            out GameWorldActionStatus duplicateDoorStatus) &&
            duplicateDoorStatus == GameWorldActionStatus.StaleCommand,
            "A duplicate interaction command was not fenced by its actor sequence.");
        host.Tick();

        const int movementCommands = 45;
        var movementTicks = new double[movementCommands];
        var offline = new PlayableOfflineDirectLink(runtime.Game, entities.PlayerId);
        int chunkCrossings = 0;
        SpatialChunkCoord previousChunk = playerPosition.Chunk;
        long movementAllocated = 0;
        for (int step = 0; step < movementCommands; step++)
        {
            Expect(input.TryCreateCommand(new ClientWorldInputSample(
                1, 0, GamePlayerActions.None, true), out GamePlayerCommand command),
                "A movement command could not be formed.");
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            Expect(offline.ApplyMovement(command) == GameWorldActionStatus.Accepted,
                "Authoritative movement was rejected.");
            host.Tick();
            movementTicks[step] = ToNanoseconds(Stopwatch.GetTimestamp() - started);
            movementAllocated += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            playerPosition = offline.ReadPosition();
            if (playerPosition.Chunk != previousChunk)
            {
                chunkCrossings++;
                AdvancePlayableProjection(runtime.Game, client, playerPosition);
                previousChunk = playerPosition.Chunk;
            }
        }
        Expect(chunkCrossings == 1 && playerPosition.Chunk.X == 1,
            "The playable path did not cross exactly one world chunk boundary.");

        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, generationId, game) => game.WorldActions.MoveItemToNearbyContainer(
                world, game.WorldState, game.Inventory,
                ResolveInWorld(world, entities.PlayerId),
                ResolveInWorld(world, entities.BreadId),
                ResolveInWorld(world, entities.BackpackId),
                ResolveInWorld(world, entities.CabinChestId),
                out _),
            out GameWorldActionStatus chestStatus) && chestStatus == GameWorldActionStatus.Accepted,
            "The nearby cabin-chest transfer was rejected.");
        host.Tick();
        RefreshPlayableProjection(runtime.Game, client);

        string preSaveCanonical = CapturePlayableCanonical(runtime.Game, entities);
        string preSaveProjection = CaptureProjectionCanonical(client);
        PlayableClientProjection rebuilt = BuildPlayableProjection(runtime.Game, playerPosition);
        Expect(CaptureProjectionCanonical(rebuilt) == preSaveProjection,
            "Retiring/rebuilding the local client projection changed visible semantic truth.");

        var vertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
        var indices = new ushort[TerrainChunkFormat.TriangleIndexCount];
        var triangleMaterials = new ushort[TerrainChunkFormat.TriangleCount];
        Expect(rebuilt.Cache.TryGetResidentHandle(
            playerPosition.ToChunkKey(GameWorldState.DefaultSpace), out ClientSemanticChunkHandle homeHandle) &&
            rebuilt.Cache.TryBuildResidentTerrainMesh(
                homeHandle, vertices, indices, triangleMaterials,
                out TerrainBaseMeshSummary meshSummary,
                out ClientSemanticChunkError semanticError,
                out TerrainBaseMeshBuildError meshError) &&
            semanticError == ClientSemanticChunkError.None &&
            meshError == TerrainBaseMeshBuildError.None &&
            meshSummary.VertexCount == TerrainChunkFormat.HeightVertexCount,
            "The rebuilt client projection could not prepare its terrain mesh.");

        var persistence = new GameRuntimePersistence(
            runtime.Game,
            composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        long saveStarted = Stopwatch.GetTimestamp();
        GameSaveResult saved = persistence.TrySave(
            new SaveSetContext(1, 1_700_000_810_001), static capture => capture());
        long saveTicks = Stopwatch.GetTimestamp() - saveStarted;
        Expect(saved.Succeeded, "The T0.10 world save failed.");

        Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.Interact, true), out GamePlayerCommand releaseHeld),
            "The held-action release sample failed.");
        Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.None, true), out _),
            "The door-action release command failed.");
        Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.Interact, true), out GamePlayerCommand closeDoor),
            "The divergent close-door command failed.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, generationId, game) =>
            {
                GameWorldActionStatus closed = game.WorldActions.ToggleCabinDoor(
                    world, game.WorldState,
                    ResolveInWorld(world, entities.PlayerId),
                    ResolveInWorld(world, entities.CabinDoorId), closeDoor, out _);
                InventoryMutationResult moved = game.Inventory.Move(
                    world,
                    ResolveInWorld(world, entities.BreadId),
                    ResolveInWorld(world, entities.CabinChestId),
                    ResolveInWorld(world, entities.BackpackId));
                return closed == GameWorldActionStatus.Accepted && moved.Accepted;
            }, out bool diverged) && diverged,
            "The post-save playable world did not diverge.");
        host.Tick();
        Expect(CapturePlayableCanonical(runtime.Game, entities) != preSaveCanonical,
            "The divergent door/inventory state matched the save point.");

        long loadStarted = Stopwatch.GetTimestamp();
        GameLoadResult loaded = persistence.TryLoad();
        long loadTicks = Stopwatch.GetTimestamp() - loadStarted;
        Expect(loaded.Succeeded, "The T0.10 save could not restore.");
        Expect(CapturePlayableCanonical(runtime.Game, entities) == preSaveCanonical,
            "The restored woods/cabin authority differs from the saved state.");
        Expect(admission.TryGetSession(entered.Participant, out AdmissionResult invalidated) &&
            invalidated.Rejection == AdmissionRejection.GenerationReplaced,
            "The local accountless session retained a stale generation entity after restore.");

        WorldPosition restoredPosition = ReadWorldPosition(runtime.Game, entities.PlayerId);
        PlayableClientProjection restored = BuildPlayableProjection(runtime.Game, restoredPosition);
        Expect(CaptureProjectionCanonical(restored) == preSaveProjection,
            "The restored client-visible world differs from the saved client projection.");

        long identityIndexBytes = 0;
        long worldReservedBytes = 0;
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (_, _, game) =>
            {
                identityIndexBytes = game.WorldState.BaseWorld!.StructuralIdentityIndexReservedBytes;
                worldReservedBytes = game.WorldState.GetSnapshot().ReservedBytes;
                return true;
            }, out bool measured) && measured,
            "T0.10 world memory evidence could not be captured.");

        Array.Sort(movementTicks);
        ClientWorldSemanticCacheSnapshot clientSnapshot = restored.Cache.GetSnapshot();
        ClientPresentationWindowSnapshot windowSnapshot = restored.Window.GetSnapshot();
        string canonicalHash = HashText(preSaveCanonical + '|' + preSaveProjection);
        return new TestMetrics(
            canonicalHash,
            ToNanoseconds(saveTicks),
            ToNanoseconds(loadTicks),
            0, 0, 0,
            saved.Manifest?.Artifacts.Sum(static artifact => artifact.ByteCount) ?? 0,
            0,
            movementTicks.Average(),
            Percentile(movementTicks, 0.95),
            0,
            movementAllocated,
            "Preauthored 5x5 woods/cabin; OfflineDirect neutral input; one chunk crossing; " +
            "door + spatially validated chest interaction; bounded 3x3 presentation; terrain mesh prep; " +
            "projection retire/rebuild; four-artifact save/diverge/restore; accountless session invalidation.",
            pack.Length,
            identityIndexBytes,
            worldReservedBytes,
            clientSnapshot.ReservedBytes,
            clientSnapshot.ResidentChunks,
            windowSnapshot.ActiveChunks,
            input.Snapshot().CommandsProduced,
            chunkCrossings,
            movementTicks.Average(),
            Percentile(movementTicks, 0.95),
            movementAllocated);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static TestMetrics AccountBackedRoundTrip()
{
    string root = TempRoot("account-backed");
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_0001));
    var composition = new UltraSimGameComposition(new InventoryLimits(32, 128, 32));
    var renderer = new RecordingRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    using var ui = new RecordingUiPort(link);
    try
    {
        using var host = new InternalHost(composition, RuntimeOptions(identity));
        host.Start();
        host.SetPlaybackRate(2);
        ServerRuntime runtime = host.RuntimeForTesting;
        FixtureKeys keys = PopulateAccountBacked(runtime.Game);

        Expect(client.CachedDefinitionCount == 0, "The CharacterSelect definition cache was not initially cold.");
        using var admission = new AdmissionService(runtime.Game, composition, ui);
        ParticipantId aliceParticipant = new(1);
        AdmissionResult waiting = admission.JoinAccountBacked(
            aliceParticipant,
            new TrustedLocalIdentity("alice-local"));
        Expect(waiting.IsWaitingForCharacter, "Account-backed admission did not enter CharacterSelect.");
        Expect(ui.ColdOpenCount == 1 && ui.HotOpenCount == 0,
            "The first CharacterSelect open did not use the cold path.");
        ReadOnlySpan<UiListRow> characterRows = client.GetRows(
            waiting.CharacterSelectView,
            CharacterSelectUi.Characters);
        Expect(characterRows.Length == 1 && characterRows[0].Values[1].Text == "Alice",
            "CharacterSelect did not project Alice.");
        client.SubmitAction(waiting.CharacterSelectView, CharacterSelectUi.Play, characterRows[0].Id);
        Expect(admission.TryGetSession(aliceParticipant, out AdmissionResult entered) && entered.IsEntered,
            "The Play action did not enter Alice.");
        Expect(Resolve(runtime.Game, keys.Alice) == entered.Entity,
            "Admission did not resolve Alice's durable entity key.");

        ParticipantId retainedSelector = new(2);
        AdmissionResult retainedWaiting = admission.JoinAccountBacked(
            retainedSelector,
            new TrustedLocalIdentity("alice-local"));
        Expect(retainedWaiting.IsWaitingForCharacter,
            "The retained selector session was not established for the hot-reopen gate.");

        using var inventory = new InventoryUiController(runtime.Game, ui);
        Entity backpack = Resolve(runtime.Game, keys.Backpack);
        InventoryUiOpenResult opened = inventory.Open(backpack);
        Expect(opened.Succeeded, "The initial inventory open failed.");
        ExpectRowNames(client.GetRows(opened.View, InventoryUi.Items), "Bread", "Iron Sword");

        Entity splitEntity = Entity.Null;
        inventory.MutationCompleted += result =>
        {
            if (result.Accepted && result.CreatedItem.IsValid)
                splitEntity = result.CreatedItem;
        };
        UiRowId breadRow = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Bread");
        client.SubmitAction(opened.View, InventoryUi.Select, breadRow);
        client.SubmitAction(opened.View, InventoryUi.Split);
        Expect(splitEntity.IsValid, "The inventory split did not publish its created item.");
        Expect(ui.LastListKinds.SequenceEqual([UiListDeltaKind.Update, UiListDeltaKind.Add]),
            "Split did not publish exactly Update + Add row deltas.");
        Expect(!ui.LastListKinds.Contains(UiListDeltaKind.Replace),
            "Split regressed to a full-list replacement.");
        keys = keys with { SplitBread = GetPersistentId(runtime.Game, splitEntity) };

        UiRowId swordRow = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Iron Sword");
        client.SubmitAction(opened.View, InventoryUi.Select, swordRow);
        client.SubmitAction(opened.View, InventoryUi.Equip);
        Expect(ui.LastListKinds.SequenceEqual([UiListDeltaKind.Remove]),
            "Equip did not publish exactly one Remove row delta.");
        Expect(!ui.LastListKinds.Contains(UiListDeltaKind.Replace),
            "Equip regressed to a full-list replacement.");
        ExpectRowNames(client.GetRows(opened.View, InventoryUi.Items), "Bread", "Bread");

        const int savedTicks = 12;
        for (int tick = 0; tick < savedTicks; tick++) host.Tick();
        Expect(ReadSimulation(runtime.Game, keys.Alice).TickMarker == savedTicks,
            "The real scheduled simulation system did not update Alice once per tick.");
        ulong savedSimulationTick = runtime.SimulationTick;
        long savedWorldTime = runtime.WorldTimeMicroseconds;
        string savedCanonical = CaptureCanonical(runtime, keys);

        var persistence = new GameRuntimePersistence(
            runtime.Game,
            composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        long pauseTicks = 0;
        long saveAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long saveStarted = Stopwatch.GetTimestamp();
        long captureCallStarted = Stopwatch.GetTimestamp();
        Task<GameSaveResult> saveTask = persistence.TrySaveAsync(
            new SaveSetContext(1, 1_700_000_800_001),
            capture =>
            {
                long started = Stopwatch.GetTimestamp();
                capture();
                pauseTicks += Stopwatch.GetTimestamp() - started;
            });
        long captureCallTicks = Stopwatch.GetTimestamp() - captureCallStarted;
        // The immutable capture is now detached: authoritative execution is
        // legal while encoding, hashing and durable publication run elsewhere.
        host.Tick();
        GameSaveResult saved = saveTask.GetAwaiter().GetResult();
        host.SetPlaybackRate(4);
        long saveTicks = Stopwatch.GetTimestamp() - saveStarted;
        long saveAllocated = GC.GetAllocatedBytesForCurrentThread() - saveAllocatedBefore;
        Expect(saved.Succeeded && saved.Manifest is not null,
            "The account-backed save generation was not accepted.");
        Expect(saved.Manifest!.Artifacts.Length == 4 &&
            saved.Manifest.Artifacts.Any(static artifact => artifact.Key == "world") &&
            saved.Manifest.Artifacts.Any(static artifact => artifact.Key == "worldview") &&
            saved.Manifest.Artifacts.Any(static artifact => artifact.Key == "accounting") &&
            saved.Manifest.Artifacts.Any(static artifact => artifact.Key == "clock"),
            "The game save did not contain ECS, current-world, Accounting, and authoritative clock participants.");
        Expect(captureCallTicks >= pauseTicks,
            "The capture-pause measurement exceeded the complete synchronous capture call.");

        Entity oldAlice = Resolve(runtime.Game, keys.Alice);
        Entity oldBackpack = Resolve(runtime.Game, keys.Backpack);
        DivergeAfterSave(runtime.Game, keys);
        for (int tick = 0; tick < 7; tick++) host.Tick();
        string divergentCanonical = CaptureCanonical(runtime, keys, allowMissingItems: true);
        Expect(divergentCanonical != savedCanonical,
            "The live generation did not diverge visibly after save.");

        long loadAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long loadStarted = Stopwatch.GetTimestamp();
        GameLoadResult loaded = persistence.TryLoad();
        long loadTicks = Stopwatch.GetTimestamp() - loadStarted;
        long loadAllocated = GC.GetAllocatedBytesForCurrentThread() - loadAllocatedBefore;
        Expect(loaded.Succeeded && loaded.GenerationId > 1,
            "The staged game generation was not published.");
        Expect(runtime.SimulationTick == savedSimulationTick &&
            runtime.WorldTimeMicroseconds == savedWorldTime,
            "Restore did not recover the authoritative saved clock state.");
        Expect(runtime.PlaybackRate == 4,
            "Restore incorrectly replaced current host playback policy with save-time policy.");
        Entity restoredAlice = Resolve(runtime.Game, keys.Alice);
        Entity restoredBackpack = Resolve(runtime.Game, keys.Backpack);
        Expect(restoredAlice != oldAlice && restoredAlice.World != oldAlice.World,
            "The restored Alice reused the old runtime entity identity.");
        Expect(restoredBackpack != oldBackpack && restoredBackpack.World != oldBackpack.World,
            "The restored Backpack reused the old runtime entity identity.");
        Expect(IsNotAlive(runtime.Game, oldAlice) && IsNotAlive(runtime.Game, oldBackpack),
            "Old-generation entity handles resolved in the replacement world.");
        string restoredCanonical = CaptureCanonical(runtime, keys);
        Expect(restoredCanonical == savedCanonical,
            "The restored logical game state differs from the accepted save state.");
        Expect(admission.TryGetSession(aliceParticipant, out AdmissionResult rebound) &&
            rebound.IsEntered && rebound.Entity == restoredAlice,
            "The entered admission session did not rebind to the replacement generation.");

        int hotBefore = ui.HotOpenCount;
        AdmissionResult hotSelector = admission.ReopenCharacterSelect(retainedSelector);
        Expect(hotSelector.IsWaitingForCharacter && ui.HotOpenCount == hotBefore + 1,
            "CharacterSelect did not reopen through the exact cached hot path.");
        ReadOnlySpan<UiListRow> hotCharacters = client.GetRows(
            hotSelector.CharacterSelectView,
            CharacterSelectUi.Characters);
        Expect(hotCharacters.Length == 1 && hotCharacters[0].Values[1].Text == "Alice",
            "The hot CharacterSelect model did not project restored Alice.");

        Expect(inventory.Close(opened.View), "The pre-restore inventory view did not close.");
        hotBefore = ui.HotOpenCount;
        InventoryUiOpenResult hotInventory = inventory.OpenHot(restoredBackpack);
        Expect(hotInventory.Succeeded && ui.HotOpenCount == hotBefore + 1,
            "Inventory did not reopen through its cached hot path.");
        ExpectRowNames(client.GetRows(hotInventory.View, InventoryUi.Items), "Bread", "Bread");

        host.Stop();
        Expect(host.State == ServerRuntimeState.Stopped, "InternalHost did not stop gracefully.");

        var headlessComposition = new UltraSimGameComposition(new InventoryLimits(32, 128, 32));
        using var headless = new HeadlessHost(
            headlessComposition,
            RuntimeOptions(identity),
            new HostCapabilities(new NullHostLogSink()));
        headless.Start();
        var headlessPersistence = new GameRuntimePersistence(
            headless.RuntimeForTesting.Game,
            headlessComposition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        GameLoadResult headlessLoaded = headlessPersistence.TryLoad();
        Expect(headlessLoaded.Succeeded, "HeadlessHost could not load the accepted save generation.");
        Expect(headless.SimulationTick == savedSimulationTick &&
            headless.WorldTimeMicroseconds == savedWorldTime,
            "A fresh HeadlessHost did not restore the saved authoritative clock.");
        Expect(headless.PlaybackRate == 1,
            "A fresh HeadlessHost inherited save-time playback policy.");
        string headlessCanonical = CaptureCanonical(headless.RuntimeForTesting, keys);
        Expect(headlessCanonical == restoredCanonical,
            "InternalHost and HeadlessHost produced different canonical logical state.");
        headless.Shutdown();
        Expect(headless.State == ServerRuntimeState.Disposed,
            "HeadlessHost did not complete explicit shutdown.");

        long artifactBytes = saved.Manifest.Artifacts.Sum(static artifact => artifact.ByteCount);
        return new TestMetrics(
            CanonicalHash: restoredCanonical,
            SaveMeanNanoseconds: ToNanoseconds(saveTicks),
            LoadMeanNanoseconds: ToNanoseconds(loadTicks),
            CapturePauseNanoseconds: ToNanoseconds(captureCallTicks),
            SaveAllocatedBytes: saveAllocated,
            LoadAllocatedBytes: loadAllocated,
            ArtifactBytes: artifactBytes,
            DirectTickMeanNanoseconds: 0,
            HostedTickMeanNanoseconds: 0,
            HostedTickP95Nanoseconds: 0,
            TickWrapperRatio: 0,
            TimedTickAllocatedBytes: 0,
            Notes: "ECS+Accounting+Clock; owner-thread immutable capture then durable worker publication; cold CharacterSelect/Inventory; row deltas; staged restore; session rebind; Internal/Headless generation parity.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static TestMetrics AccountlessRoundTrip()
{
    string root = TempRoot("accountless");
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_0002));
    var composition = new UltraSimGameComposition(new InventoryLimits(16, 32, 8));
    try
    {
        using var host = new HeadlessHost(
            composition,
            RuntimeOptions(identity),
            new HostCapabilities(new NullHostLogSink()));
        host.Start();
        ServerRuntime runtime = host.RuntimeForTesting;
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(new ParticipantId(11));
        Expect(entered.IsEntered && entered.Entity.IsValid,
            "Accountless admission did not produce a playable ECS entity.");
        PersistentEntityId id = GetPersistentId(runtime.Game, entered.Entity);
        Expect(GetAccountingCounts(runtime.Game) == (0, 0),
            "Accountless admission created Accounting records.");
        for (int tick = 0; tick < 4; tick++) host.Tick();
        Expect(ReadSimulation(runtime.Game, id).TickMarker == 4,
            "The accountless playable did not participate in real scheduled simulation.");

        var persistence = new GameRuntimePersistence(
            runtime.Game,
            composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        GameSaveResult save = persistence.TrySave(
            new SaveSetContext(1, 1_700_000_800_002),
            static capture => capture());
        Expect(save.Succeeded, "The declared accountless composition save set failed.");
        Entity old = entered.Entity;
        GameLoadResult load = persistence.TryLoad();
        Expect(load.Succeeded, "The accountless composition could not restore its save set.");
        Entity restored = Resolve(runtime.Game, id);
        Expect(restored != old && restored.World != old.World && IsNotAlive(runtime.Game, old),
            "Accountless restore did not retire the old runtime entity identity.");
        Expect(ReadSimulation(runtime.Game, id).TickMarker == 4,
            "Accountless restore did not recover scheduled component state.");
        Expect(GetAccountingCounts(runtime.Game) == (0, 0),
            "Accountless restore invented Accounting state.");
        Expect(admission.TryGetSession(entered.Participant, out AdmissionResult invalidated) &&
            invalidated.Rejection == AdmissionRejection.GenerationReplaced,
            "An accountless admission session retained a stale generation-local entity after restore.");
        host.Shutdown();

        return new TestMetrics(
            HashText($"{identity.WorldId.Value}:{id.Value}:4:0:0"),
            0, 0, 0, 0, 0,
            save.Manifest?.Artifacts.Sum(static artifact => artifact.ByteCount) ?? 0,
            0, 0, 0, 0, 0,
            "Accountless admission used no account records; the composition's declared empty Accounting and authoritative Clock participants remained round-trippable.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static TestMetrics AuthoritativeClockRoundTripsAcrossHostCadences()
{
    string root = TempRoot("cross-cadence-clock");
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_0004));
    try
    {
        var sourceComposition = new UltraSimGameComposition(new InventoryLimits(8, 16, 4));
        using var source = new InternalHost(
            sourceComposition,
            RuntimeOptions(identity, frequencyHz: 60, worldTimeRate: 7));
        source.Start();
        Expect(source.RuntimeForTesting.Game.TryUseActiveGeneration<IGameGenerationStateProvider, PersistentEntityId>(
            (world, _, game) =>
            {
                if (!world.TryInstantiate(game.Content.Character, out Entity character))
                    throw new InvalidOperationException("Cross-cadence character creation failed.");
                return world.GetOrAssignPersistentId(character);
            },
            out PersistentEntityId characterId),
            "Cross-cadence character bootstrap did not run.");
        source.Tick();
        PersistentWorldClockState at60 = CapturePersistentClock(source.RuntimeForTesting);
        Expect(at60.SimulationTick == 1 &&
            at60.WorldTimeMicroseconds == 116_666 &&
            at60.WorldTimeFractionQuanta == 2_400_000,
            "The 60 Hz source did not produce the expected exact fractional world time.");
        var sourcePersistence = new GameRuntimePersistence(
            source.RuntimeForTesting.Game,
            sourceComposition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        GameSaveResult firstSave = sourcePersistence.TrySave(
            new SaveSetContext(1, 1_700_000_800_004),
            static capture => capture());
        Expect(firstSave.Succeeded, "The 60 Hz source save failed.");

        var at20Composition = new UltraSimGameComposition(new InventoryLimits(8, 16, 4));
        using var at20Host = new HeadlessHost(
            at20Composition,
            RuntimeOptions(identity, frequencyHz: 20, worldTimeRate: 7),
            new HostCapabilities(new NullHostLogSink()));
        at20Host.Start();
        var at20Persistence = new GameRuntimePersistence(
            at20Host.RuntimeForTesting.Game,
            at20Composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        Expect(at20Persistence.TryLoad().Succeeded, "The 20 Hz host could not load the 60 Hz save.");
        Expect(at20Host.SimulationFrequencyHz == 20,
            "Restore replaced the 20 Hz host cadence with the save producer cadence.");
        Expect(CapturePersistentClock(at20Host.RuntimeForTesting) == at60,
            "60 -> 20 Hz restore changed authoritative clock state.");
        Expect(ReadSimulation(at20Host.RuntimeForTesting.Game, characterId).TickMarker == 1,
            "60 -> 20 Hz restore changed durable ECS/game state.");

        at20Host.Tick();
        PersistentWorldClockState after20Step = CapturePersistentClock(at20Host.RuntimeForTesting);
        Expect(after20Step.SimulationTick == 2 &&
            after20Step.WorldTimeMicroseconds == 466_666 &&
            after20Step.WorldTimeFractionQuanta == 2_400_000,
            "The restored clock did not advance exactly at 20 Hz.");
        Expect(ReadSimulation(at20Host.RuntimeForTesting.Game, characterId).TickMarker == 2,
            "The restored ECS/game state did not execute the next 20 Hz step.");
        GameSaveResult secondSave = at20Persistence.TrySave(
            new SaveSetContext(2, 1_700_000_800_005),
            static capture => capture());
        Expect(secondSave.Succeeded, "The 20 Hz source save failed.");

        var at60Composition = new UltraSimGameComposition(new InventoryLimits(8, 16, 4));
        using var at60Host = new HeadlessHost(
            at60Composition,
            RuntimeOptions(identity, frequencyHz: 60, worldTimeRate: 7),
            new HostCapabilities(new NullHostLogSink()));
        at60Host.Start();
        var at60Persistence = new GameRuntimePersistence(
            at60Host.RuntimeForTesting.Game,
            at60Composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        Expect(at60Persistence.TryLoad().Succeeded, "The 60 Hz host could not load the 20 Hz save.");
        Expect(at60Host.SimulationFrequencyHz == 60,
            "Restore replaced the 60 Hz host cadence with the save producer cadence.");
        Expect(CapturePersistentClock(at60Host.RuntimeForTesting) == after20Step,
            "20 -> 60 Hz restore changed authoritative clock state.");
        Expect(ReadSimulation(at60Host.RuntimeForTesting.Game, characterId).TickMarker == 2,
            "20 -> 60 Hz restore changed durable ECS/game state.");

        return new TestMetrics(
            HashText($"{after20Step.SimulationTick}:{after20Step.WorldTimeMicroseconds}:" +
                $"{after20Step.WorldTimeFractionQuanta}"),
            0, 0, 0, 0, 0,
            secondSave.Manifest?.Artifacts.Sum(static artifact => artifact.ByteCount) ?? 0,
            0, 0, 0, 0, 0,
            "Full ECS+Accounting+Clock save/load at 60->20 and 20->60 Hz; host cadence remained local policy and fixed-point clock state remained exact.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static TestMetrics RuntimeTickBenchmark()
{
    const int population = 4_096;
    const int warmupTicks = 256;
    const int ticksPerSample = 256;
    const int samples = 21;
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_0003));

    var directComposition = new UltraSimGameComposition(
        new InventoryLimits(8, population + 8, 8),
        maximumChunksPerContentArchetype: 16);
    GameStaging direct = directComposition.CreateFresh(population + 16, identity);
    try
    {
        direct.World.Seal();
        direct.World.Initialize();
        direct.World.BeginRun();
        for (int index = 0; index < population; index++)
            Expect(direct.World.TryInstantiate(direct.Content.Character, out _), "Direct benchmark population failed.");
        direct.WorldState.Initialize(direct.World);

        var hostedComposition = new UltraSimGameComposition(
            new InventoryLimits(8, population + 8, 8),
            maximumChunksPerContentArchetype: 16);
        using var host = new InternalHost(hostedComposition, RuntimeOptions(identity, population + 16));
        host.Start();
        Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, generationId, game) =>
            {
                for (int index = 0; index < population; index++)
                {
                    if (!world.TryInstantiate(game.Content.Character, out _)) return false;
                }
                return true;
            },
            out bool populated) && populated,
            "Hosted benchmark population failed.");

        // Intentional fixed-60 performance control. Variable/adaptive cadence
        // is disabled in both paths so cadence cannot conceal a regression.
        Action directTick = () =>
        {
            direct.World.Tick(1d / 60d);
            direct.WorldState.CommitAfterEcsStep(direct.World);
        };
        Action hostedTick = host.Tick;

        for (int tick = 0; tick < warmupTicks; tick++)
        {
            directTick();
            hostedTick();
        }

        double[] directSamples = new double[samples];
        double[] hostedSamples = new double[samples];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < samples; sample++)
        {
            if ((sample & 1) == 0)
            {
                directSamples[sample] = MeasureTicks(ticksPerSample, directTick);
                hostedSamples[sample] = MeasureTicks(ticksPerSample, hostedTick);
            }
            else
            {
                hostedSamples[sample] = MeasureTicks(ticksPerSample, hostedTick);
                directSamples[sample] = MeasureTicks(ticksPerSample, directTick);
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        double[] pairedRatios = directSamples.Zip(
            hostedSamples,
            static (direct, hosted) => hosted / Math.Max(1d, direct)).ToArray();
        Array.Sort(pairedRatios);
        Array.Sort(directSamples);
        Array.Sort(hostedSamples);
        double directMean = directSamples.Average();
        double hostedMean = hostedSamples.Average();
        double hostedP95 = Percentile(hostedSamples, 0.95);
        double ratio = Percentile(pairedRatios, 0.50);
        Expect(allocated == 0, $"Warmed direct/hosted ticks allocated {allocated} caller bytes.");
        Expect(ratio <= 1.05,
            $"The paired-median hosted runtime wrapper tax was {ratio:P1}, exceeding the 5% acceptance margin.");

        host.Stop();
        return new TestMetrics(
            HashText($"{population}:{warmupTicks}:{ticksPerSample}:{samples}"),
            0, 0, 0, 0, 0, 0,
            directMean,
            hostedMean,
            hostedP95,
            ratio,
            allocated,
            "21 alternating paired A/B samples; ratio is paired median; fixed 60 Hz, cadence changes/adaptive off, playback 1x; 4,096 scheduled rows; 256 ticks/sample; all caller/worker execution is serial on the owner thread.");
    }
    finally
    {
        direct.WorldState.Dispose();
        direct.Inventory.Dispose();
        direct.World.Dispose();
    }
}

static PlayableClientProjection BuildPlayableProjection(
    ServerGameFacet runtime,
    WorldPosition playerPosition)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, PlayableClientProjection>(
        (_, _, game) =>
        {
            WorldPreparedView prepared = game.WorldState.PreparedWorld ??
                throw new InvalidOperationException("The playable world has no prepared structural view.");
            var cache = new ClientWorldSemanticCache(
                prepared,
                new ClientWorldSemanticCacheLimits(
                    MaximumResidentChunks: 12,
                    MaximumFrozenStaticsPerChunk: 16,
                    MaximumMultiFragmentsPerChunk: 16,
                    MaximumPublicRecordsPerChunk: 16,
                    MaximumKnownPublicIdentities: 64));
            StageWindow(game.WorldState.Publications, cache, playerPosition.Chunk, radius: 1);
            var window = new ClientPresentationWindow(cache, presentationRadiusChunks: 1);
            Span<ClientPresentationChange> changes = stackalloc ClientPresentationChange[18];
            Expect(window.TryPlanTrack(
                playerPosition, changes,
                out ClientPresentationWindowPlan plan,
                out ClientPresentationWindowTransition transition,
                out int required,
                out ClientPresentationWindowError error),
                $"The initial presentation plan failed: {error} ({required}).");
            Expect(transition.Entered == 9 && transition.ActiveChunks == 9,
                "The initial presentation transition did not enter exactly nine chunks.");
            window.Commit(plan);
            return new PlayableClientProjection(cache, window);
        }, out PlayableClientProjection projection),
        "The playable client projection could not be built.");
    return projection;
}

static void AdvancePlayableProjection(
    ServerGameFacet runtime,
    PlayableClientProjection projection,
    WorldPosition playerPosition)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
        (_, _, game) =>
        {
            StageMissingWindowChunks(
                game.WorldState.Publications, projection.Cache, playerPosition.Chunk, radius: 1);
            Span<ClientPresentationChange> changes = stackalloc ClientPresentationChange[18];
            Expect(projection.Window.TryPlanTrack(
                playerPosition, changes,
                out ClientPresentationWindowPlan plan,
                out ClientPresentationWindowTransition transition,
                out int required,
                out ClientPresentationWindowError error),
                $"A crossing presentation plan failed: {error} ({required}).");
            Expect(transition.Entered == 3 && transition.Exited == 3,
                "A one-chunk cardinal crossing did not produce a 3/6/3 window transition.");
            projection.Window.Commit(plan);
            for (int index = 0; index < required; index++)
            {
                if (changes[index].Kind != ClientPresentationChangeKind.Exit) continue;
                Expect(projection.Cache.BeginRetire(changes[index].Handle) &&
                    projection.Cache.CompleteRetire(changes[index].Handle),
                    "An exited semantic chunk could not retire.");
            }
            return true;
        }, out bool advanced) && advanced,
        "The playable projection did not advance.");
}

static void RefreshPlayableProjection(
    ServerGameFacet runtime,
    PlayableClientProjection projection)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
        (_, _, game) =>
        {
            Span<ClientPresentationBinding> bindings = stackalloc ClientPresentationBinding[9];
            Expect(projection.Window.TryCopyBindings(bindings, out int count, out _),
                "Presentation bindings could not be borrowed for refresh.");
            Span<WorldPublicRecord> records = stackalloc WorldPublicRecord[16];
            for (int index = 0; index < count; index++)
            {
                ref readonly ClientPresentationBinding binding = ref bindings[index];
                ChunkPublicationReadStatus status = game.WorldState.Publications.TryCopySnapshot(
                    binding.Key, records, out ChunkPublicationBatch batch);
                if (status == ChunkPublicationReadStatus.Snapshot)
                {
                    Expect(projection.Cache.TryApplySnapshot(
                        binding.SemanticHandle, batch, records[..batch.Count], out _),
                        $"Snapshot refresh failed for {binding.Key}.");
                }
                else
                {
                    Expect(status == ChunkPublicationReadStatus.ChunkAbsent &&
                        projection.Cache.TryApplyChunkAbsent(
                            binding.SemanticHandle,
                            game.WorldState.Publications.Epoch,
                            batch,
                            out _),
                        $"Absent refresh failed for {binding.Key}.");
                }
            }
            return true;
        }, out bool refreshed) && refreshed,
        "The playable projection did not refresh.");
}

static void StageWindow(
    ChunkPublicationStore publications,
    ClientWorldSemanticCache cache,
    SpatialChunkCoord center,
    int radius)
{
    for (int z = center.Z - radius; z <= center.Z + radius; z++)
    {
        for (int x = center.X - radius; x <= center.X + radius; x++)
            StageChunk(publications, cache, new SpatialChunkKey(GameWorldState.DefaultSpace, x, z));
    }
}

static void StageMissingWindowChunks(
    ChunkPublicationStore publications,
    ClientWorldSemanticCache cache,
    SpatialChunkCoord center,
    int radius)
{
    for (int z = center.Z - radius; z <= center.Z + radius; z++)
    {
        for (int x = center.X - radius; x <= center.X + radius; x++)
        {
            var key = new SpatialChunkKey(GameWorldState.DefaultSpace, x, z);
            if (!cache.TryGetResidentHandle(key, out _)) StageChunk(publications, cache, key);
        }
    }
}

static void StageChunk(
    ChunkPublicationStore publications,
    ClientWorldSemanticCache cache,
    SpatialChunkKey key)
{
    Expect(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out ClientSemanticChunkError requestError),
        $"Semantic request failed for {key}: {requestError}.");
    Span<WorldPublicRecord> records = stackalloc WorldPublicRecord[16];
    ChunkPublicationReadStatus status = publications.TryCopySnapshot(key, records, out ChunkPublicationBatch batch);
    if (status == ChunkPublicationReadStatus.Snapshot)
    {
        Expect(cache.TryStageFromBaseWorld(handle, records[..batch.Count], batch.Cursor,
            out ClientSemanticChunkError stageError),
            $"Semantic snapshot stage failed for {key}: {stageError}.");
    }
    else
    {
        ClientSemanticChunkError stageError = default;
        Expect(status == ChunkPublicationReadStatus.ChunkAbsent &&
            cache.TryStageFromBaseWorld(handle, [], default, out stageError),
            $"Semantic base-only stage failed for {key}: {stageError}.");
    }
    Expect(cache.TryPublish(handle, out ClientSemanticChunkError publishError),
        $"Semantic publication failed for {key}: {publishError}.");
}

static string CapturePlayableCanonical(
    ServerGameFacet runtime,
    GamePlayableWorldEntities entities)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, string>(
        (world, _, game) =>
        {
            Entity player = ResolveInWorld(world, entities.PlayerId);
            Entity door = ResolveInWorld(world, entities.CabinDoorId);
            Entity bread = ResolveInWorld(world, entities.BreadId);
            Expect(game.WorldState.TryReadPosition(world, player, out WorldPosition playerPosition),
                "The playable position is missing.");
            Expect(game.WorldActions.TryReadCabinDoor(world, door, out bool isOpen),
                "The cabin door state is missing.");
            Expect(game.WorldState.TryReadPublic(world, door, out WorldPublicDescriptor doorPublic),
                "The cabin door public state is missing.");
            PersistentEntityId parentId = default;
            Expect(game.Inventory.TryReadItem(world, bread, out InventoryItem item) &&
                world.TryGetPersistentId(item.Parent, out parentId),
                "The moved inventory item has no durable parent.");
            string text = $"{game.WorldState.BaseWorld!.Fingerprint}|" +
                $"{playerPosition.GlobalXMillimeters}:{playerPosition.YMillimeters}:{playerPosition.GlobalZMillimeters}|" +
                $"door={isOpen}:{doorPublic.Orientation.Units}|bread-parent={parentId.Value}|" +
                $"overlay={game.WorldState.Overlay?.Revision.Value ?? 0}";
            return HashText(text);
        }, out string canonical),
        "The playable canonical state could not be captured.");
    return canonical;
}

static string CaptureProjectionCanonical(PlayableClientProjection projection)
{
    Span<ClientPresentationBinding> bindings = stackalloc ClientPresentationBinding[9];
    Expect(projection.Window.TryCopyBindings(bindings, out int count, out _),
        "The presentation projection could not expose its bindings.");
    var text = new StringBuilder(64 * 1024);
    var publicRows = new List<string>();
    for (int index = 0; index < count; index++)
    {
        ref readonly ClientPresentationBinding binding = ref bindings[index];
        Expect(projection.Cache.TryGetResidentView(
            binding.SemanticHandle, out ClientSemanticChunkView view),
            $"The client semantic view for {binding.Key} is missing.");
        text.Append("|chunk=").Append(binding.Key.Coord.X).Append(',').Append(binding.Key.Coord.Z);
        foreach (ushort material in view.TerrainMaterials) text.Append(':').Append(material);
        foreach (int height in view.TerrainAbsoluteHeightsMillimeters) text.Append(':').Append(height);
        foreach (WorldFrozenStaticRecord value in view.FrozenStatics)
        {
            text.Append("|s=").Append(value.Id.Value).Append(':').Append(value.DefinitionId.Value)
                .Append(':').Append(value.Position.GlobalXMillimeters)
                .Append(':').Append(value.Position.YMillimeters)
                .Append(':').Append(value.Position.GlobalZMillimeters);
        }
        foreach (WorldMultiFragmentRecord value in view.MultiFragments)
        {
            text.Append("|m=").Append(value.MultiId.Value).Append(':').Append(value.PieceId.Value)
                .Append(':').Append(value.DefinitionId.Value)
                .Append(':').Append(value.Position.GlobalXMillimeters)
                .Append(':').Append(value.Position.YMillimeters)
                .Append(':').Append(value.Position.GlobalZMillimeters);
        }
        foreach (WorldPublicRecord value in view.PublicRecords)
        {
            publicRows.Add($"{binding.Key.Coord.X},{binding.Key.Coord.Z}:" +
                $"{value.PrefabId}:{value.Position.GlobalXMillimeters}:" +
                $"{value.Position.YMillimeters}:{value.Position.GlobalZMillimeters}:" +
                $"{value.Orientation.Units}:{(ushort)value.StateFlags}");
        }
    }
    publicRows.Sort(StringComparer.Ordinal);
    foreach (string value in publicRows) text.Append("|p=").Append(value);
    return HashText(text.ToString());
}

static WorldPosition ReadWorldPosition(ServerGameFacet runtime, PersistentEntityId id)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, WorldPosition>(
        (world, _, game) =>
        {
            Expect(game.WorldState.TryReadPosition(
                world, ResolveInWorld(world, id), out WorldPosition position),
                "The requested world position is missing.");
            return position;
        }, out WorldPosition result),
        "The world-position read did not run.");
    return result;
}

static FixtureKeys PopulateAccountBacked(ServerGameFacet runtime)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, FixtureKeys>(
        (world, generationId, game) =>
        {
            Expect(world.TryInstantiate(game.Content.Character, out Entity alice), "Alice creation failed.");
            Expect(game.Inventory.TryCreateOwnedContainer(
                world, game.Content.Backpack, alice, 8, out Entity backpack), "Backpack creation failed.");
            Expect(game.Inventory.TryCreateItem(
                world, backpack, game.Content.Bread, 5, out Entity bread), "Bread creation failed.");
            Expect(game.Inventory.TryCreateItem(
                world, backpack, game.Content.IronSword, 1, out Entity sword), "Sword creation failed.");
            PersistentEntityId aliceId = world.GetOrAssignPersistentId(alice);
            PersistentEntityId backpackId = world.GetOrAssignPersistentId(backpack);
            PersistentEntityId breadId = world.GetOrAssignPersistentId(bread);
            PersistentEntityId swordId = world.GetOrAssignPersistentId(sword);
            Expect(world.TryGetPrefabProvenance(alice, out PrefabKey prefab), "Alice has no prefab provenance.");
            var account = new AccountId(1);
            game.Accounting.AddAccount(new AccountRecord(account));
            game.Accounting.BindTrustedIdentity(new TrustedLocalIdentity("alice-local"), account);
            game.Accounting.AddCharacter(new CharacterRecord(
                new CharacterId(1),
                account,
                0,
                "Alice",
                prefab,
                new PersistentEntityKey(world.PersistentWorldId, aliceId)));
            return new FixtureKeys(aliceId, backpackId, breadId, swordId, default);
        },
        out FixtureKeys keys),
        "The account-backed fixture did not run against the active game generation.");
    return keys;
}

static void DivergeAfterSave(ServerGameFacet runtime, FixtureKeys keys)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
        (world, generationId, game) =>
        {
            Entity sword = ResolveInWorld(world, keys.Sword);
            Entity split = ResolveInWorld(world, keys.SplitBread);
            Entity backpack = ResolveInWorld(world, keys.Backpack);
            Expect(game.Inventory.DestroyItem(world, sword).Accepted,
                "The post-save sword divergence failed.");
            Expect(game.Inventory.DestroyItem(world, split).Accepted,
                "The post-save split-stack divergence failed.");
            Expect(game.Inventory.TryCreateItem(
                world, backpack, game.Content.Apple, 3, out Entity apple),
                "The post-save Apple divergence failed.");
            _ = world.GetOrAssignPersistentId(apple);
            return true;
        },
        out bool diverged) && diverged,
        "Post-save divergence did not run.");
}

static string CaptureCanonical(
    ServerRuntime runtime,
    FixtureKeys keys,
    bool allowMissingItems = false)
{
    ulong simulationTick = runtime.SimulationTick;
    long worldTime = runtime.WorldTimeMicroseconds;
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, string>(
        (world, _, game) =>
        {
            var text = new StringBuilder(512);
            text.Append("tick=").Append(simulationTick)
                .Append("|world-time=").Append(worldTime)
                .Append("|world=").Append(world.PersistentWorldId.Value);
            Entity alice = ResolveInWorld(world, keys.Alice);
            Expect(game.TryReadSimulationState(world, alice, out GameSimulationState simulation),
                "Alice has no simulation state.");
            text.Append("|alice=").Append(keys.Alice.Value)
                .Append(':').Append(simulation.TickMarker);

            AccountingSnapshot accounting = game.Accounting.CaptureSnapshot();
            foreach (AccountRecord account in accounting.Accounts)
                text.Append("|account=").Append(account.AccountId.Value).Append(':').Append((byte)account.Status);
            foreach (CharacterRecord character in accounting.Characters)
            {
                text.Append("|character=").Append(character.CharacterId.Value)
                    .Append(':').Append(character.AccountId.Value)
                    .Append(':').Append(character.Slot)
                    .Append(':').Append(character.DisplayName)
                    .Append(':').Append(character.PrefabKey.Value)
                    .Append(':').Append(character.PersistentEntityKey.World.Value)
                    .Append(':').Append(character.PersistentEntityKey.Entity.Value);
            }

            InventoryIndexSnapshot index = game.Inventory.Index.CaptureSnapshot();
            text.Append("|index=").Append(index.ContainerCount)
                .Append(':').Append(index.ItemCount)
                .Append(':').Append(index.ContainedItemCount);
            AppendContainer(text, world, game.Inventory, keys.Backpack);
            AppendItem(text, world, game.Inventory, keys.Bread, allowMissingItems);
            AppendItem(text, world, game.Inventory, keys.SplitBread, allowMissingItems);
            AppendItem(text, world, game.Inventory, keys.Sword, allowMissingItems);
            return HashText(text.ToString());
        },
        out string canonical),
        "Canonical state capture did not run.");
    return canonical;
}

static void AppendContainer(
    StringBuilder text,
    EcsWorld world,
    InventoryGenerationState inventory,
    PersistentEntityId containerId)
{
    Entity container = ResolveInWorld(world, containerId);
    InventoryItemView[] rows = inventory.Snapshot(world, container);
    text.Append("|container=").Append(containerId.Value).Append('[');
    foreach (InventoryItemView row in rows)
    {
        text.Append(row.PersistentId.Value).Append(':')
            .Append(row.Prefab.Value).Append(':')
            .Append((byte)row.EquipSlot).Append(':')
            .Append(row.Quantity).Append(';');
    }
    text.Append(']');
}

static void AppendItem(
    StringBuilder text,
    EcsWorld world,
    InventoryGenerationState inventory,
    PersistentEntityId id,
    bool allowMissing)
{
    if (!world.TryResolvePersistentEntity(id, out Entity entity))
    {
        if (!allowMissing) throw new InvalidOperationException($"Persistent item {id.Value} is missing.");
        text.Append("|item=").Append(id.Value).Append(":missing");
        return;
    }
    Expect(inventory.TryReadItem(world, entity, out InventoryItem item),
        $"Persistent item {id.Value} has no InventoryItem component.");
    Expect(world.TryGetPersistentId(item.Parent, out PersistentEntityId parent),
        $"Persistent item {id.Value} has a nonpersistent parent.");
    Expect(world.TryGetPrefabProvenance(entity, out PrefabKey prefab),
        $"Persistent item {id.Value} has no prefab provenance.");
    text.Append("|item=").Append(id.Value)
        .Append(':').Append(prefab.Value)
        .Append(':').Append(parent.Value)
        .Append(':').Append((byte)item.EquipSlot)
        .Append(':').Append(item.Quantity);
}

static GameSimulationState ReadSimulation(ServerGameFacet runtime, PersistentEntityId id)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, GameSimulationState>(
        (world, _, game) =>
        {
            Entity entity = ResolveInWorld(world, id);
            Expect(game.TryReadSimulationState(world, entity, out GameSimulationState value),
                "The playable entity has no simulation state.");
            return value;
        },
        out GameSimulationState state),
        "Simulation read did not run.");
    return state;
}

static (int Accounts, int Characters) GetAccountingCounts(ServerGameFacet runtime)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, (int, int)>(
        (_, _, game) => (game.Accounting.AccountCount, game.Accounting.CharacterCount),
        out (int, int) counts),
        "Accounting count read did not run.");
    return counts;
}

static PersistentEntityId GetPersistentId(ServerGameFacet runtime, Entity entity)
{
    Expect(runtime.TryUseActiveGeneration(
        (world, _) => world.GetOrAssignPersistentId(entity),
        out PersistentEntityId id),
        "Persistent identity assignment did not run.");
    return id;
}

static PersistentWorldClockState CapturePersistentClock(ServerRuntime runtime)
{
    Expect(runtime.Game.TryCapturePersistentClockState(out PersistentWorldClockState state),
        "The active persistent clock state could not be captured.");
    return state;
}

static Entity Resolve(ServerGameFacet runtime, PersistentEntityId id)
{
    Expect(runtime.TryUseActiveGeneration(
        (world, generationId) => ResolveInWorld(world, id),
        out Entity entity),
        "Persistent entity resolution did not run.");
    return entity;
}

static Entity ResolveInWorld(EcsWorld world, PersistentEntityId id)
{
    if (!world.TryResolvePersistentEntity(id, out Entity entity))
        throw new InvalidOperationException($"Persistent entity {id.Value} did not resolve.");
    return entity;
}

static bool IsNotAlive(ServerGameFacet runtime, Entity entity)
{
    Expect(runtime.TryUseActiveGeneration(
        (world, _) => !world.IsAlive(entity),
        out bool stale),
        "Stale-handle validation did not run.");
    return stale;
}

static ServerRuntimeOptions RuntimeOptions(
    PersistenceIdentityOptions identity,
    int capacity = 512,
    int frequencyHz = 60,
    int worldTimeRate = 24) => new(
    UltraSimGameComposition.CreateWorldOptions(capacity, identity),
    new WorldClockOptions(simulationFrequencyHz: frequencyHz, worldTimeRate: worldTimeRate),
    allowVariablePlayback: true);

static PersistenceRestoreOptions RestoreOptions() => new(
    new PersistenceLimits(
        maximumArtifactBytes: 16 * 1024 * 1024,
        maximumEntities: 100_000,
        maximumShapes: 64,
        maximumComponents: 64,
        maximumSections: 256,
        maximumComponentPayloadBytes: 8 * 1024 * 1024,
        maximumTemporaryBytes: 64 * 1024 * 1024),
    ChunkLayoutOptions.KiB16);

static double MeasureTicks(int count, Action tick)
{
    long started = Stopwatch.GetTimestamp();
    for (int index = 0; index < count; index++) tick();
    return ToNanoseconds(Stopwatch.GetTimestamp() - started) / count;
}

static double Percentile(double[] sorted, double fraction) =>
    sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];

static double ToNanoseconds(long stopwatchTicks) =>
    stopwatchTicks * (1_000_000_000d / Stopwatch.Frequency);

static string HashText(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static void ExpectRowNames(ReadOnlySpan<UiListRow> rows, params string[] expected)
{
    string[] actual = rows.ToArray()
        .Select(static row => row.Values.Length == 0 ? string.Empty : row.Values[0].Text ?? string.Empty)
        .Order(StringComparer.Ordinal)
        .ToArray();
    string[] orderedExpected = expected.Order(StringComparer.Ordinal).ToArray();
    Expect(actual.SequenceEqual(orderedExpected),
        $"Expected UI rows [{string.Join(", ", orderedExpected)}], got [{string.Join(", ", actual)}].");
}

static UiRowId FindRow(ReadOnlySpan<UiListRow> rows, string name)
{
    foreach (UiListRow row in rows)
    {
        if (row.Values.Length != 0 && row.Values[0].Text == name) return row.Id;
    }
    throw new InvalidOperationException($"UI row '{name}' was not found.");
}

static void Run(string name, Func<TestMetrics> body, List<TestResult> results)
{
    long started = Stopwatch.GetTimestamp();
    TestMetrics metrics = body();
    double elapsed = ToNanoseconds(Stopwatch.GetTimestamp() - started);
    results.Add(new TestResult(name, true, elapsed, metrics));
    Console.WriteLine($"PASS {name}");
}

static string TempRoot(string name) =>
    Path.Combine(Path.GetTempPath(), "ultrasim-s0-8-" + name + '-' + Guid.NewGuid().ToString("N"));

static string? ReadOption(string[] values, string key)
{
    for (int index = 0; index + 1 < values.Length; index++)
    {
        if (StringComparer.Ordinal.Equals(values[index], key)) return values[index + 1];
    }
    return null;
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

readonly record struct FixtureKeys(
    PersistentEntityId Alice,
    PersistentEntityId Backpack,
    PersistentEntityId Bread,
    PersistentEntityId Sword,
    PersistentEntityId SplitBread);

sealed record AcceptanceResult(
    string Workload,
    DateTimeOffset CapturedAtUtc,
    string Runtime,
    int LogicalProcessorCount,
    TestResult[] Tests);

sealed record TestResult(
    string Name,
    bool Passed,
    double TotalNanoseconds,
    TestMetrics Metrics);

sealed record TestMetrics(
    string CanonicalHash,
    double SaveMeanNanoseconds,
    double LoadMeanNanoseconds,
    double CapturePauseNanoseconds,
    long SaveAllocatedBytes,
    long LoadAllocatedBytes,
    long ArtifactBytes,
    double DirectTickMeanNanoseconds,
    double HostedTickMeanNanoseconds,
    double HostedTickP95Nanoseconds,
    double TickWrapperRatio,
    long TimedTickAllocatedBytes,
    string Notes,
    long WorldPackBytes = 0,
    long StructuralIdentityIndexBytes = 0,
    long WorldStateReservedBytes = 0,
    long ClientSemanticReservedBytes = 0,
    int ClientResidentChunks = 0,
    int PresentationActiveChunks = 0,
    ulong InputCommands = 0,
    int ChunkCrossings = 0,
    double WholePipelineMeanNanoseconds = 0,
    double WholePipelineP95Nanoseconds = 0,
    long WholePipelineAllocatedBytes = 0);

sealed record PlayableClientProjection(
    ClientWorldSemanticCache Cache,
    ClientPresentationWindow Window);

sealed class PlayableOfflineDirectLink
{
    private readonly ServerGameFacet _runtime;
    private readonly PersistentEntityId _playerId;
    private readonly ServerGenerationStateOperation<IGameGenerationStateProvider, GameWorldActionStatus> _apply;
    private readonly ServerGenerationStateOperation<IGameGenerationStateProvider, WorldPosition> _read;
    private GamePlayerCommand _command;

    internal PlayableOfflineDirectLink(ServerGameFacet runtime, PersistentEntityId playerId)
    {
        _runtime = runtime;
        _playerId = playerId;
        _apply = ApplyCore;
        _read = ReadCore;
    }

    internal GameWorldActionStatus ApplyMovement(in GamePlayerCommand command)
    {
        _command = command;
        if (!_runtime.TryUseActiveGeneration(_apply, out GameWorldActionStatus status))
            throw new InvalidOperationException("OfflineDirect movement had no active generation.");
        return status;
    }

    internal WorldPosition ReadPosition()
    {
        if (!_runtime.TryUseActiveGeneration(_read, out WorldPosition position))
            throw new InvalidOperationException("OfflineDirect position read had no active generation.");
        return position;
    }

    private GameWorldActionStatus ApplyCore(
        EcsWorld world,
        ulong generationId,
        IGameGenerationStateProvider game)
    {
        if (!world.TryResolvePersistentEntity(_playerId, out Entity player))
            return GameWorldActionStatus.MissingActor;
        return game.WorldActions.ApplyMovement(
            world, game.WorldState, player, _command, out _);
    }

    private WorldPosition ReadCore(
        EcsWorld world,
        ulong generationId,
        IGameGenerationStateProvider game)
    {
        if (!world.TryResolvePersistentEntity(_playerId, out Entity player) ||
            !game.WorldState.TryReadPosition(world, player, out WorldPosition position))
            throw new InvalidOperationException("OfflineDirect player position is missing.");
        return position;
    }
}

sealed class NullHostLogSink : IHostLogSink
{
    public void Write(HostLogLevel level, string message) { }
}

sealed class RecordingUiPort : IUiSemanticPort, IDisposable
{
    private readonly LocalSemanticLink _link;
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];
    private bool _disposed;

    internal RecordingUiPort(LocalSemanticLink link)
    {
        _link = link;
        _link.ActionSubmitted += Forward;
    }

    internal int ColdOpenCount { get; private set; }
    internal int HotOpenCount { get; private set; }
    internal UiListDeltaKind[] LastListKinds { get; private set; } = [];

    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
        _actions.Add(instance, handler);
    public void UnbindActions(UiViewInstanceId instance) => _actions.Remove(instance);

    public UiViewInstanceId OpenView(UiOpenView operation)
    {
        ColdOpenCount++;
        return _link.OpenView(operation);
    }

    public UiViewInstanceId OpenHotView(UiHotOpenView operation)
    {
        HotOpenCount++;
        return _link.OpenHotView(operation);
    }

    public void UpdateView(UiUpdateView operation)
    {
        LastListKinds = operation.Delta.Lists.ToArray()
            .Select(static patch => patch.Kind)
            .ToArray();
        _link.UpdateView(operation);
    }

    public bool CloseView(UiCloseView operation) => _link.CloseView(operation);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _link.ActionSubmitted -= Forward;
        _actions.Clear();
    }

    private void Forward(UiActionRequest request)
    {
        if (_actions.TryGetValue(request.Instance, out Action<UiActionRequest>? handler)) handler(request);
    }
}

sealed class RecordingRenderer : IUiRenderer
{
    internal int Creates { get; private set; }
    internal int Applies { get; private set; }
    internal int Closes { get; private set; }

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        Creates++;
        return new Handle();
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction) => Applies++;

    public void Close(IUiRenderHandle handle) => Closes++;

    private sealed class Handle : IUiRenderHandle;
}
