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
using UltraSim.Game.Physics;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Server.Hosting;
using UltraSim.Server.Persistence;
using UltraSim.Framework.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;
using UltraSim.UI;
using UltraSim.World.Testing;

const string Workload = "ultrasim-t0.10-integrated-acceptance-v9";
string? output = ReadOption(args, "--output");

var results = new List<TestResult>();
Run("PlayableWoodsCabinOfflineDirectRoundTrip", PlayableWoodsCabinRoundTrip, results);
Run("W5CanonicalTwoPackAuthorityAndPersistence", W5CanonicalTwoPackAuthorityAndPersistence, results);
Run("ConceptDemoLegacySaveFailsClosedUnderCanonicalAuthority",
    ConceptDemoLegacySaveFailsClosedUnderCanonicalAuthority, results);
Run("PlayableCabinHasCanonicalRectangularShell", PlayableCabinHasCanonicalRectangularShell, results);
Run("P9CabinVisualBenchmarkIsSeparateAndStructurallyTwoRoom",
    P9CabinVisualBenchmarkIsSeparateAndStructurallyTwoRoom, results);
Run("PlayableAuthorityBlocksCabinDoorWallsAndChest", PlayableAuthorityBlocksCabinDoorWallsAndChest, results);
Run("ConceptDemoWorldCombinesFloatingBaseCaveAndSkyShells",
    ConceptDemoWorldCombinesFloatingBaseCaveAndSkyShells, results);
Run("ConceptDemoTelepadUsesAuthoritativeDestination",
    ConceptDemoTelepadUsesAuthoritativeDestination, results);
Run("ConceptDemoFarGateUsesSparseAuthoredGeography",
    ConceptDemoFarGateUsesSparseAuthoredGeography, results);
Run("ConceptDemoInformationalSignValidatesAuthoritativeTargetAndRange",
    ConceptDemoInformationalSignValidatesAuthoritativeTargetAndRange, results);
Run("ConceptDemoFallbackLabKeepsPresentationFailureOutOfAuthority",
    ConceptDemoFallbackLabKeepsPresentationFailureOutOfAuthority, results);
Run("SimplePhysicsIsFreshAndRebuildsOpenDoorProjection", SimplePhysicsGenerationReplacement, results);
Run("SimplePhysicsOccupancyIsFirstClassBoundedAndAllocationFree", SimplePhysicsOccupancy, results);
Run("SimplePhysicsSweepPreventsTunnelingAndCornerCutting", SimplePhysicsSweptMotion, results);
Run("SimplePhysicsSelectsTerrainShellSupportStepAndClearance",
    SimplePhysicsTerrainShellSupport, results);
Run("SimplePhysicsResolvesAirborneTerrainShellContacts",
    SimplePhysicsAirborneTerrainShellContacts, results);
Run("SimplePhysicsStaticTopsProvideGroundedSupport",
    SimplePhysicsStaticSupport, results);
Run("SimplePhysicsSeparatesBodyAndStaticTopSupportPolicy",
    SimplePhysicsStaticTopPolicy, results);
Run("AuthoritativeJumpFallLandingAndRecovery",
    AuthoritativeJumpFallLandingAndRecovery, results);
Run("AuthoritativeAirborneWallContactKeepsGravityProgress",
    AuthoritativeAirborneWallContactKeepsGravityProgress, results);
Run("AuthoritativeAirbornePreparedBoundaryKeepsGravityProgress",
    AuthoritativeAirbornePreparedBoundaryKeepsGravityProgress, results);
Run("AuthoritativeVerticalArithmeticOverflowRecovers",
    AuthoritativeVerticalArithmeticOverflowRecovers, results);
Run("VerticalMomentumIsGenerationLocalAcrossRestore",
    VerticalMomentumIsGenerationLocalAcrossRestore, results);
Run("VerticalStateFencesReusedEntitySlots",
    VerticalStateFencesReusedEntitySlots, results);
Run("SweptMotionPreservesDirectAndDestinationPolicy", SweptMotionPreservesGamePolicy, results);
Run("PlayableMovementIsCadenceIndependent", PlayableMovementCadenceMatrix, results);
Run("PlayableMovementIsInputDeliveryRateIndependent", PlayableMovementDeliveryRateMatrix, results);
Run("PlayableDestinationIsLatchedCadenceIndependentAndExact", PlayableDestinationCadenceMatrix, results);
Run("PlayableDestinationProgressIsCadenceIndependent", PlayableDestinationProgressCadenceMatrix, results);
Run("LocomotionUpdatesComposeAndSupersede", LocomotionUpdateSemantics, results);
Run("AuthoritativeRelocationCancelsOperationalLocomotion", AuthoritativeRelocationCancelsLocomotion, results);
Run("AuthoritativeRelocationRejectsBlockedTargetsTransactionally", BlockedRelocationIsTransactional, results);
Run("IndependentMoveLookKeepsWorldMovementAndFacingSeparate", IndependentMoveLookKeepsWorldMovementAndFacingSeparate, results);
Run("ControlSessionLineageFencesRetainedDirect", ControlSessionLineageFencesRetainedDirect, results);
Run("ContradictoryRestoredDoorFailsClosed", ContradictoryRestoredDoorFailsClosed, results);
Run("DynamicCollisionProjectionCapacityFailsClosed", DynamicCollisionProjectionCapacityFailsClosed, results);
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

static TestMetrics W5CanonicalTwoPackAuthorityAndPersistence()
{
    string root = TempRoot("w5-two-pack");
    try
    {
        var (baseComposition, west, east) = W5TwoPackFixture.Create();
        Expect(baseComposition.TryValidateComplete(out _), "W5 authored composition failed complete validation.");
        PreparedWorldComposition world = WithOverlay(baseComposition, west, east);
        Expect(world.TryValidateComplete(out _), "W5 overlaid composition failed complete validation.");
        var composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32), preparedComposition: world);
        using var host = new InternalHost(composition,
            RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x5755_0001))));
        host.Start();
        ServerRuntime runtime = host.RuntimeForTesting;
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0x5755));
        Expect(entered.IsEntered, "W5 playable admission failed.");
        var start = new WorldPosition(127, 0, 15_000, 8_000, 0);
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (ecs, _, game) => game.WorldState.TrySetPosition(ecs, entered.Entity, start),
            out bool placed) && placed, "W5 actor setup failed.");
        PersistentEntityId durableActor = GetPersistentId(runtime.Game, entered.Entity);
        host.Tick();
        var input = new ClientWorldInputSequencer();
        var link = new PlayableOfflineDirectLink(runtime.Game, durableActor, input.ControlSession);
        WorldPublicEntityId publicId = default;
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (ecs, _, game) => game.WorldState.Publications.TryGetPublicEntityId(entered.Entity, out publicId),
            out bool publicPresent) && publicPresent, "W5 actor did not publish a public identity.");
        Expect(ReadHome().Coord.X == 127, "W5 actor did not start in chunk 127.");
        Expect(CheckPhysicsAcrossSeam(), "W5 Physics occupancy/support/sweep failed across the seam.");
        Cross(1, 128);
        Cross(-1, 127);
        Cross(1, 128);
        WorldPosition savedPosition = link.ReadPosition();
        Expect(savedPosition.Chunk.X == 128, "W5 save point was not east of the Pack seam.");
        var persistence = new GameRuntimePersistence(runtime.Game, composition,
            RestoreOptions(), new SaveSetCoordinator(root));
        GameSaveResult saved = persistence.TrySave(new SaveSetContext(1, 1_700_000_000_001),
            static capture => capture());
        Expect(saved.Succeeded, $"W5 save failed: {saved.Failure}");
        Cross(-1, 127);
        ulong generationBeforeFailedRestore = ReadGeneration();
        WorldPosition positionBeforeFailedRestore = link.ReadPosition();
        var unavailableContent = new GameRuntimePersistence(runtime.Game, composition,
            RestoreOptions(), new SaveSetCoordinator(root),
            structuralManifestResolver: _ => world.Manifest,
            structuralPackResolver: (_, _) => null);
        GameLoadResult refused = unavailableContent.TryLoad();
        Expect(!refused.Succeeded && refused.Failure is not null &&
            ReadGeneration() == generationBeforeFailedRestore &&
            link.ReadPosition() == positionBeforeFailedRestore,
            "W5 failed exact-Pack resolution partially published a generation.");
        GameLoadResult loaded = persistence.TryLoad();
        Expect(loaded.Succeeded, $"W5 restore failed: {loaded.Failure}");
        WorldPosition restored = ReadWorldPosition(runtime.Game, durableActor);
        Expect(restored == savedPosition && restored.Chunk.X == 128,
            "W5 actor global position was not restored after seam crossing.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (ecs, _, game) =>
            {
                PreparedWorldComposition? restoredWorld = game.WorldState.PreparedComposition;
                return restoredWorld is not null &&
                    restoredWorld.ManifestFingerprint == world.ManifestFingerprint &&
                    restoredWorld.Revision == world.Revision &&
                    restoredWorld.TryValidateComplete(out PreparedWorldCompositionError validationError) &&
                    restoredWorld.TryGetPreparedPack(W5TwoPackFixture.WestCoord, out PreparedWorldPack w) &&
                    restoredWorld.TryGetPreparedPack(W5TwoPackFixture.EastCoord, out PreparedWorldPack e) &&
                    w.View.BaseWorldFingerprint == west.Fingerprint &&
                    e.View.BaseWorldFingerprint == east.Fingerprint &&
                    w.View.Overlay.Revision == new WorldOverlayRevision(1) &&
                    e.View.Overlay.Revision == new WorldOverlayRevision(1) &&
                    w.View.Overlay.TryGetChunkRevision(new SpatialChunkKey(world.Space, 125, 0),
                        out WorldChunkContentRevision westRevision, out WorldChunkContentHash westHash) &&
                    e.View.Overlay.TryGetChunkRevision(new SpatialChunkKey(world.Space, 130, 0),
                        out WorldChunkContentRevision eastRevision, out WorldChunkContentHash eastHash) &&
                    westRevision.IsValid && eastRevision.IsValid && westHash.IsValid && eastHash.IsValid;
            }, out bool restoredComposition) && restoredComposition,
            "W5 exact manifest, revision, and independent per-Pack overlays did not restore.");
        return new TestMetrics(HashText($"{world.ManifestFingerprint}|{savedPosition}|{publicId}"),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            "W5 real controlled movement crosses 127/128 in both directions; worldview/v2 restores exact global position and two overlays.",
            ChunkCrossings: 4);

        ulong ReadGeneration()
        {
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, ulong>(
                (_, generation, _) => generation, out ulong value),
                "W5 active generation identity was unavailable.");
            return value;
        }

        bool CheckPhysicsAcrossSeam()
        {
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (_, _, game) =>
                {
                    var body = new SimplePhysicsBody(200, 200, 100, 100, 1_700, 300, 300);
                    var before = new WorldPosition(127, 0, 15_800, 8_000, 0);
                    var after = new WorldPosition(128, 0, 200, 8_000, 0);
                    SimpleOccupancyResult first = game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(before, body), [], true);
                    SimpleOccupancyResult last = game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(after, body), [], true);
                    SimpleMotionResult forward = game.SimplePhysics.ResolveMotion(
                        new SimpleMotionRequest(before, after.GlobalXMillimeters,
                            after.GlobalZMillimeters, body), [], true);
                    SimpleMotionResult reverse = game.SimplePhysics.ResolveMotion(
                        new SimpleMotionRequest(after, before.GlobalXMillimeters,
                            before.GlobalZMillimeters, body), [], true);
                    return first.CanOccupy && last.CanOccupy && forward.WasResolved &&
                        reverse.WasResolved && first.Support.IsBase && last.Support.IsBase &&
                        forward.Support.IsBase && reverse.Support.IsBase;
                }, out bool valid), "W5 Physics seam probe could not run.");
            return valid;
        }

        SpatialChunkKey ReadHome()
        {
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, SpatialChunkKey>(
                (ecs, _, game) =>
                {
                    Expect(game.WorldState.Index.TryGetHome(ResolveInWorld(ecs, durableActor), out SpatialChunkKey home),
                        "W5 actor lost spatial-index membership.");
                    return home;
                }, out SpatialChunkKey key), "W5 home lookup failed.");
            return key;
        }

        void Cross(int direction, int destinationChunk)
        {
            Expect(input.TryCreateSemanticCommand(new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDirect(
                        checked((short)(direction * GamePlayerCommand.MaximumAxisMagnitude)), 0),
                    GamePlayerActions.None, true), out GamePlayerCommand move),
                "W5 direct movement command could not form.");
            Expect(link.ApplyCommand(move).Locomotion == GameWorldActionStatus.Accepted,
                "W5 direct movement command was not admitted.");
            int remaining = 250;
            while (link.ReadPosition().Chunk.X != destinationChunk && remaining-- > 0)
                host.Tick();
            Expect(link.ReadPosition().Chunk.X == destinationChunk,
                $"W5 controlled movement did not enter chunk {destinationChunk}.");
            Expect(ReadHome().Coord.X == destinationChunk,
                "W5 spatial index did not follow ordinary chunk migration.");
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (ecs, _, game) => game.WorldState.Publications.TryGetPublicEntityId(
                    ResolveInWorld(ecs, durableActor), out WorldPublicEntityId current) && current == publicId,
                out bool samePublic) && samePublic,
                "W5 public identity changed at a Pack boundary.");
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (_, _, game) =>
                {
                    var records = new WorldPublicRecord[8];
                    bool targetPresent = game.WorldState.Publications.TryCopySnapshot(
                        new SpatialChunkKey(world.Space, destinationChunk, 0), records,
                        out ChunkPublicationBatch target) == ChunkPublicationReadStatus.Snapshot;
                    bool foundId = false;
                    for (int index = 0; index < target.Count; index++)
                        foundId |= records[index].Id == publicId;
                    int sourceChunk = destinationChunk == 127 ? 128 : 127;
                    ChunkPublicationReadStatus sourceStatus = game.WorldState.Publications.TryCopySnapshot(
                        new SpatialChunkKey(world.Space, sourceChunk, 0), records,
                        out ChunkPublicationBatch source);
                    bool oldGone = sourceStatus == ChunkPublicationReadStatus.ChunkAbsent;
                    if (sourceStatus == ChunkPublicationReadStatus.Snapshot)
                    {
                        oldGone = true;
                        for (int index = 0; index < source.Count; index++)
                            oldGone &= records[index].Id != publicId;
                    }
                    return targetPresent && foundId && oldGone;
                }, out bool publicationMoved) && publicationMoved,
                "W5 ordinary source retirement/target publication did not follow the actor.");
        }
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    static PreparedWorldComposition WithOverlay(PreparedWorldComposition source,
        BoundWorldPack west, BoundWorldPack east)
    {
        var packs = new PreparedWorldPack[2];
        BoundWorldPack[] bases = [west, east];
        WorldPackCoord[] coords = [W5TwoPackFixture.WestCoord, W5TwoPackFixture.EastCoord];
        for (int index = 0; index < 2; index++)
        {
            BoundWorldPack baseWorld = bases[index];
            var builder = new WorldMutableOverlayBuilder(baseWorld);
            var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
            Array.Fill(materials, (ushort)(index == 0 ? 2 : 3));
            var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
            Expect(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase),
                "W5 overlay height base is invalid.");
            Expect(builder.TryReplaceTerrain(new SpatialChunkKey(baseWorld.Space, index == 0 ? 125 : 130, 0),
                materials, heightBase, heights), "W5 overlay replacement was rejected.");
            Expect(builder.TryBuild(out WorldMutableOverlay? overlay) && overlay is not null,
                "W5 per-Pack overlay fixture could not build.");
            packs[index] = new PreparedWorldPack(coords[index], new WorldPreparedView(baseWorld, overlay));
        }
        Expect(PreparedWorldComposition.TryCreate(source.Space, new WorldCompositionRevision(3),
            source.Manifest, packs, out PreparedWorldComposition? prepared, out _),
            "W5 overlay composition could not build.");
        return prepared!;
    }
}

static TestMetrics ConceptDemoLegacySaveFailsClosedUnderCanonicalAuthority()
{
    string root = TempRoot("w5r-legacy-concept-save");
    try
    {
        WorldPack legacy = GamePlayableWorld.CreateConceptDemoWorldPack();
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_D000UL));
        var former = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32), baseWorldPack: legacy);
        using (var oldHost = new InternalHost(former, RuntimeOptions(identity)))
        {
            oldHost.Start();
            var persistence = new GameRuntimePersistence(oldHost.RuntimeForTesting.Game,
                former, RestoreOptions(), new SaveSetCoordinator(root));
            GameSaveResult saved = persistence.TrySave(
                new SaveSetContext(1, 1_700_000_000_001), static capture => capture());
            Expect(saved.Succeeded, $"Pre-W5R Concept Demo save setup failed: {saved.Failure}");
        }
        ConceptDemoStructuralSource source = GamePlayableWorld.CreateConceptDemoStructuralSource();
        var current = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32), conceptDemoStructuralSource: source);
        using var host = new InternalHost(current, RuntimeOptions(identity));
        host.Start();
        ulong generationBefore = 0;
        Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<IGameGenerationStateProvider, ulong>(
            (_, generation, _) => generation, out generationBefore),
            "Canonical generation was unavailable before legacy restore.");
        var loader = new GameRuntimePersistence(host.RuntimeForTesting.Game,
            current, RestoreOptions(), new SaveSetCoordinator(root));
        GameLoadResult refused = loader.TryLoad();
        Expect(!refused.Succeeded && refused.Failure is PersistenceCompatibilityException &&
            refused.Failure.Message.Contains("Pre-W5R Concept Demo worldview/v1", StringComparison.Ordinal),
            $"Legacy combined save did not fail with an explicit compatibility error: {refused.Failure}");
        Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (_, generation, game) => generation == generationBefore &&
                game.WorldState.PreparedWorld is null &&
                game.WorldState.PreparedComposition?.ManifestFingerprint == source.Manifest.Fingerprint,
            out bool unchanged) && unchanged,
            "Refused legacy restore altered canonical authority.");
        return new TestMetrics(source.Manifest.Fingerprint.ToString(),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            "Pre-W5R worldview/v1 combined-Pack saves fail closed without dropping possible overlay state; Cabin v1 remains separately supported.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

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
        var offline = new PlayableOfflineDirectLink(
            runtime.Game, entities.PlayerId, input.ControlSession);
        Expect(input.TryCreateCommand(new ClientWorldInputSample(
            1, 0, GamePlayerActions.Interact, true), out GamePlayerCommand openDoor),
            "The door input command could not be formed.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayerCommandResult>(
            (world, generationId, game) =>
            {
                _ = game.WorldActions.ApplyPlayerCommand(
                    world, game.WorldState,
                    ResolveInWorld(world, entities.PlayerId),
                    openDoor,
                    ResolveInWorld(world, entities.CabinDoorId),
                    out GamePlayerCommandResult commandResult);
                return commandResult;
            },
            out GamePlayerCommandResult doorResult) && doorResult.WasAdmitted &&
            doorResult.Locomotion == GameWorldActionStatus.Accepted &&
            doorResult.Interaction == GameWorldActionStatus.Accepted && doorResult.DoorIsOpen,
            "One composed move+interact command did not retain Direct intent and open the door.");
        host.Tick();
        playerPosition = offline.ReadPosition();
        Expect(playerPosition.GlobalXMillimeters ==
            GamePlayableWorld.PlayerStart.GlobalXMillimeters + 100,
            $"The retained Direct intent did not integrate on the authoritative step: {playerPosition}.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayerCommandResult>(
            (world, generationId, game) =>
            {
                _ = game.WorldActions.ApplyPlayerCommand(
                    world, game.WorldState,
                    ResolveInWorld(world, entities.PlayerId),
                    openDoor,
                    ResolveInWorld(world, entities.CabinDoorId),
                    out GamePlayerCommandResult commandResult);
                return commandResult;
            },
            out GamePlayerCommandResult duplicateDoorResult) &&
            duplicateDoorResult.Admission == GameWorldActionStatus.StaleCommand,
            "A duplicate interaction command was not fenced by its actor sequence.");

        Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.None, true), out _),
            "The interaction release sample could not be formed.");
        Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.Interact, true), out GamePlayerCommand outOfRange),
            "The out-of-range interaction sample could not be formed.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, generationId, game) =>
            {
                Entity player = ResolveInWorld(world, entities.PlayerId);
                Entity door = ResolveInWorld(world, entities.CabinDoorId);
                Expect(game.WorldState.TryReadPosition(world, player, out WorldPosition savedPosition),
                    "The player position was unavailable for replay fencing.");
                Expect(game.WorldState.TrySetPosition(world, player, GamePlayableWorld.AnimalPosition),
                    "The replay-fencing actor could not move out of range.");
                GameWorldActionStatus first = game.WorldActions.ApplyPlayerCommand(
                    world, game.WorldState, player, outOfRange,
                    door, out GamePlayerCommandResult firstResult);
                Expect(game.WorldState.TrySetPosition(world, player, savedPosition),
                    "The replay-fencing actor position could not be restored.");
                GameWorldActionStatus replay = game.WorldActions.ApplyPlayerCommand(
                    world, game.WorldState, player, outOfRange,
                    door, out _);
                return first == GameWorldActionStatus.Accepted &&
                    firstResult.Interaction == GameWorldActionStatus.OutOfRange &&
                    replay == GameWorldActionStatus.StaleCommand;
            }, out bool replayFenced) && replayFenced,
            "A rejected admitted interaction acquired new meaning when replayed.");
        host.Tick();

        const int movementSteps = 45;
        var movementTicks = new double[movementSteps];
        int chunkCrossings = 0;
        SpatialChunkCoord previousChunk = playerPosition.Chunk;
        long movementAllocated = 0;
        Expect(input.TryCreateCommand(new ClientWorldInputSample(
            1, 0, GamePlayerActions.None, true), out GamePlayerCommand retainedDirect) &&
            offline.ApplyMovement(retainedDirect) == GameWorldActionStatus.Accepted,
            "The retained Direct command could not be admitted.");
        for (int step = 0; step < movementSteps; step++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
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
        Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.None, true), out GamePlayerCommand stopDirect) &&
            offline.ApplyMovement(stopDirect) == GameWorldActionStatus.Accepted,
            "Direct release did not admit the complete stopped state.");
        WorldPosition stoppedAt = offline.ReadPosition();
        host.Tick();
        Expect(offline.ReadPosition() == stoppedAt,
            "The actor continued moving after Direct intent was released.");

        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, generationId, game) =>
            {
                Entity player = ResolveInWorld(world, entities.PlayerId);
                WorldPosition chest = GamePlayableWorld.ChestPosition;
                Expect(WorldPosition.TryFromGlobalMillimeters(
                        chest.GlobalXMillimeters - 500,
                        chest.YMillimeters,
                        chest.GlobalZMillimeters - 1_500,
                        out WorldPosition nearby) &&
                    game.WorldActions.RelocateActor(
                        world, game.WorldState, player, nearby, out _) ==
                        GameWorldActionStatus.Accepted,
                    "The player could not approach the cabin chest after the timed boundary walk.");
                return game.WorldActions.MoveItemToNearbyContainer(
                    world, game.WorldState, game.Inventory,
                    player,
                    ResolveInWorld(world, entities.BreadId),
                    ResolveInWorld(world, entities.BackpackId),
                    ResolveInWorld(world, entities.CabinChestId),
                    out _);
            },
            out GameWorldActionStatus chestStatus) && chestStatus == GameWorldActionStatus.Accepted,
            "The nearby cabin-chest transfer was rejected.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, generationId, game) =>
            {
                Entity player = ResolveInWorld(world, entities.PlayerId);
                Entity bread = ResolveInWorld(world, entities.BreadId);
                Entity backpack = ResolveInWorld(world, entities.BackpackId);
                Entity chest = ResolveInWorld(world, entities.CabinChestId);
                Expect(game.WorldState.TryReadPosition(world, chest, out WorldPosition chestPosition),
                    "The chest position was missing for authorization evidence.");
                Expect(WorldPosition.TryFromGlobalMillimeters(
                    32_000, chestPosition.YMillimeters, 12_000, out WorldPosition remotePosition) &&
                    game.WorldState.TrySetPosition(world, chest, remotePosition),
                    "The chest could not be staged remotely for authorization evidence.");
                GameWorldActionStatus rejected = game.WorldActions.MoveItemToNearbyContainer(
                    world, game.WorldState, game.Inventory,
                    player, bread, chest, backpack, out _);
                Expect(game.WorldState.TrySetPosition(world, chest, chestPosition),
                    "The chest position could not be restored after authorization evidence.");
                GameWorldActionStatus take = game.WorldActions.MoveItemToNearbyContainer(
                    world, game.WorldState, game.Inventory,
                    player, bread, chest, backpack, out _);
                GameWorldActionStatus replace = game.WorldActions.MoveItemToNearbyContainer(
                    world, game.WorldState, game.Inventory,
                    player, bread, backpack, chest, out _);
                return rejected == GameWorldActionStatus.UnauthorizedContainer &&
                    take == GameWorldActionStatus.Accepted &&
                    replace == GameWorldActionStatus.Accepted;
            }, out bool authorizationProved) && authorizationProved,
            "Container authority did not validate both the source and destination.");
        host.Tick();
        RefreshPlayableProjection(runtime.Game, client);

        Expect(input.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDestination(
                        stoppedAt.GlobalXMillimeters + 1_000,
                        stoppedAt.GlobalZMillimeters),
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand nonDurableDestination) &&
            offline.ApplyCommand(nonDurableDestination).Locomotion == GameWorldActionStatus.Accepted &&
            ReadActiveLocomotionCount(runtime.Game) == 1,
            "The pre-save non-durable Destination could not be latched.");

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
                GameWorldActionStatus closed = game.WorldActions.ApplyPlayerCommand(
                    world, game.WorldState,
                    ResolveInWorld(world, entities.PlayerId), closeDoor,
                    ResolveInWorld(world, entities.CabinDoorId), out GamePlayerCommandResult closeResult);
                InventoryMutationResult moved = game.Inventory.Move(
                    world,
                    ResolveInWorld(world, entities.BreadId),
                    ResolveInWorld(world, entities.CabinChestId),
                    ResolveInWorld(world, entities.BackpackId));
                return closed == GameWorldActionStatus.Accepted &&
                    closeResult.Interaction == GameWorldActionStatus.Accepted && moved.Accepted;
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
        Expect(ReadActiveLocomotionCount(runtime.Game) == 0,
            "Restore retained non-durable player locomotion.");
        host.Tick();
        Expect(ReadWorldPosition(runtime.Game, entities.PlayerId) == restoredPosition,
            "The restored actor resumed a non-durable Destination.");
        PlayableClientProjection restored = BuildPlayableProjection(runtime.Game, restoredPosition);
        Expect(CaptureProjectionCanonical(restored) == preSaveProjection,
            "The restored client-visible world differs from the saved client projection.");

        long identityIndexBytes = 0;
        long worldReservedBytes = 0;
        long worldActionReservedBytes = 0;
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (_, _, game) =>
            {
                identityIndexBytes = game.WorldState.BaseWorld!.StructuralIdentityIndexReservedBytes;
                worldReservedBytes = game.WorldState.GetSnapshot().ReservedBytes;
                worldActionReservedBytes = game.WorldActions.ReservedBytes;
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
            "Preauthored 5x5 woods/cabin; retained OfflineDirect world-space Direct input; " +
            "one chunk crossing; " +
            "composed movement/door command; two-endpoint-authorized chest interaction; " +
            "bounded 3x3 presentation; terrain mesh prep; " +
            "projection retire/rebuild; four-artifact save/diverge/restore; accountless session invalidation.",
            pack.Length,
            identityIndexBytes,
            worldReservedBytes,
            worldActionReservedBytes,
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

static TestMetrics PlayableCabinHasCanonicalRectangularShell()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var fragments = new List<WorldMultiFragmentRecord>(6);
    for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
    {
        for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
        {
            var key = new SpatialChunkKey(GameWorldState.DefaultSpace, x, z);
            if (!pack.TryGetMultiFragments(key, out WorldMultiFragmentChunkView chunk))
                continue;
            for (int index = 0; index < chunk.Count; index++)
            {
                WorldMultiFragmentRecord record = chunk.GetRecord(index);
                if (record.MultiId == new MultiInstanceId(1))
                    fragments.Add(record);
            }
        }
    }

    Expect(fragments.Count == 6,
        $"The authored cabin did not have five wall pieces plus one roof: {fragments.Count}.");
    WorldMultiFragmentRecord north = Piece(1);
    WorldMultiFragmentRecord west = Piece(2);
    WorldMultiFragmentRecord east = Piece(3);
    WorldMultiFragmentRecord southWest = Piece(4);
    WorldMultiFragmentRecord southEast = Piece(5);
    WorldMultiFragmentRecord roof = Piece(6);

    Expect(north.Variant == GameWorldStaticVariants.CabinWallEightMeters &&
        north.Bounds.MinimumXMillimeters == 12_000 &&
        north.Bounds.MaximumXExclusiveMillimeters == 20_001 &&
        north.Bounds.MinimumZMillimeters == 14_850 &&
        north.Bounds.MaximumZExclusiveMillimeters == 15_151,
        "The north cabin wall is not one thin eight-metre rectangle.");
    Expect(west.Variant == GameWorldStaticVariants.CabinWallSixMeters &&
        west.Orientation == WorldYaw.QuarterTurn &&
        west.Bounds.MinimumXMillimeters == 11_850 &&
        west.Bounds.MaximumXExclusiveMillimeters == 12_151 &&
        west.Bounds.MinimumZMillimeters == 9_000 &&
        west.Bounds.MaximumZExclusiveMillimeters == 15_001,
        "The west cabin wall is not one thin six-metre rectangle.");
    Expect(east.Variant == GameWorldStaticVariants.CabinWallSixMeters &&
        east.Orientation == WorldYaw.QuarterTurn &&
        east.Bounds.MinimumXMillimeters == 19_850 &&
        east.Bounds.MaximumXExclusiveMillimeters == 20_151,
        "The east cabin wall is not the matching thin rectangle.");
    Expect(southWest.Variant == GameWorldStaticVariants.CabinWallThreeMeters &&
        southEast.Variant == GameWorldStaticVariants.CabinWallThreeMeters &&
        southWest.Bounds.MaximumXExclusiveMillimeters == 15_001 &&
        southEast.Bounds.MinimumXMillimeters == 17_000,
        "The south wall does not preserve the authored two-metre doorway gap.");
    Expect((roof.Flags & WorldStaticFlags.Collidable) == 0 &&
        roof.Position.YMillimeters >= north.Bounds.MaximumYExclusiveMillimeters,
        "The presentation roof blocks the standing actor volume.");

    string canonical = string.Join('|', fragments.OrderBy(static row => row.PieceId.Value)
        .Select(static row => $"{row.PieceId.Value}:{row.DefinitionId.Value}:{row.Variant}:" +
            $"{row.Orientation.Units}:{row.Flags}:" +
            $"{row.Bounds.MinimumXMillimeters}:{row.Bounds.MinimumYMillimeters}:{row.Bounds.MinimumZMillimeters}:" +
            $"{row.Bounds.MaximumXExclusiveMillimeters}:{row.Bounds.MaximumYExclusiveMillimeters}:" +
            $"{row.Bounds.MaximumZExclusiveMillimeters}"));
    return new TestMetrics(
        HashText(canonical),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "Authored cabin is a canonical 8m x 6m thin-wall Multi with a 2m south doorway and non-blocking overhead roof.");

    WorldMultiFragmentRecord Piece(uint id) => fragments.Single(
        row => row.PieceId == new MultiPieceId(id));
}

static TestMetrics P9CabinVisualBenchmarkIsSeparateAndStructurallyTwoRoom()
{
    const string classicFingerprint =
        "80DCF456AD43FD3876D6E5BB6936FD3747E0ECDA23179407753DD393DC981B57";
    const string benchmarkV1Fingerprint =
        "B0CE72EEE73C3E014733499A9816A608280DFEF6757BD40FB599DB0318E81BDC";
    WorldPack classic = GamePlayableWorld.CreateWorldPack();
    WorldPack benchmark = GameCabinVisualBenchmarkWorldPack.Create();
    Expect(classic.Fingerprint.ToString() == classicFingerprint,
        "The accepted Classic Cabin pack changed while adding the visual benchmark.");
    Expect(benchmark.Fingerprint != classic.Fingerprint && benchmark.ChunkCount == 25,
        "The versioned benchmark must be a distinct, bounded authored WorldPack.");
    Expect(benchmark.Fingerprint.ToString() == benchmarkV1Fingerprint,
        "The versioned v1 benchmark bytes changed without a new authored identity.");
    WorldPack repeated = GameCabinVisualBenchmarkWorldPack.Create();
    Expect(repeated.Fingerprint == benchmark.Fingerprint,
        "The benchmark WorldPack is not deterministic.");
    byte[] bytes = new byte[benchmark.Length];
    benchmark.CopyTo(bytes);
    Expect(WorldPack.TryOpen(bytes, WorldPackLimits.Default, GameContent.WorldFingerprint,
        out WorldPack? reopened, out _) && reopened!.Fingerprint == benchmark.Fingerprint,
        "The benchmark WorldPack cannot be reopened from its exact bytes.");

    var pieces = new Dictionary<uint, WorldMultiFragmentRecord>();
    for (int z = -2; z <= 2; z++)
    for (int x = -2; x <= 2; x++)
    {
        if (!benchmark.TryGetMultiFragments(
                new SpatialChunkKey(GameWorldState.DefaultSpace, x, z),
                out WorldMultiFragmentChunkView fragments)) continue;
        for (int index = 0; index < fragments.Count; index++)
        {
            WorldMultiFragmentRecord piece = fragments.GetRecord(index);
            Expect(piece.MultiId == new MultiInstanceId(1) &&
                pieces.TryAdd(piece.PieceId.Value, piece),
                "The benchmark has a duplicate or unowned structural fragment.");
        }
    }
    Expect(pieces.Count == 19 && benchmark.MultiInstanceCount == 1,
        "The authored two-room shell is incomplete.");
    WorldMultiFragmentRecord westDoor = pieces[1], eastDoor = pieces[2];
    WorldMultiFragmentRecord westDivider = pieces[7], eastDivider = pieces[8];
    Expect(westDoor.Bounds.MaximumXExclusiveMillimeters == 15_001 &&
        eastDoor.Bounds.MinimumXMillimeters == 17_000 &&
        westDivider.Bounds.MaximumXExclusiveMillimeters == 15_001 &&
        eastDivider.Bounds.MinimumXMillimeters == 17_000,
        "The exterior or interior doorway does not have a real two-metre opening.");
    Expect(pieces[3].Variant == GameWorldStaticVariants.CabinWallThreeMeters &&
        pieces[4].Variant == GameWorldStaticVariants.CabinWallThreeMeters &&
        pieces[5].Variant == GameWorldStaticVariants.CabinWallThreeMeters &&
        pieces[6].Variant == GameWorldStaticVariants.CabinWallThreeMeters,
        "Room A unexpectedly has a windowed side wall.");
    Expect(pieces[12].Variant == GameWorldStaticVariants.CabinWindowSixMeters &&
        pieces[12].Flags == WorldStaticFlags.Renderable &&
        pieces.Values.Count(piece => piece.DefinitionId.Value == GameWorldStaticDefinitionIds.CabinWall &&
            piece.Variant == GameWorldStaticVariants.CabinWindowSixMeters &&
            (piece.Flags & WorldStaticFlags.Renderable) != 0) == 1,
        $"Room B must have exactly one visual window without a fake solid collider: " +
        $"variant={pieces[12].Variant} flags={pieces[12].Flags} " +
        $"visuals={pieces.Values.Count(piece => piece.DefinitionId.Value == GameWorldStaticDefinitionIds.CabinWall && piece.Variant == GameWorldStaticVariants.CabinWindowSixMeters && (piece.Flags & WorldStaticFlags.Renderable) != 0)}.");
    bool InWindow(WorldMultiFragmentRecord piece, long x, long y, long z) =>
        (piece.Flags & WorldStaticFlags.Collidable) != 0 &&
        piece.Bounds.MinimumXMillimeters <= x && x < piece.Bounds.MaximumXExclusiveMillimeters &&
        piece.Bounds.MinimumYMillimeters <= y && y < piece.Bounds.MaximumYExclusiveMillimeters &&
        piece.Bounds.MinimumZMillimeters <= z && z < piece.Bounds.MaximumZExclusiveMillimeters;
    Expect(!pieces.Values.Any(piece => InWindow(piece, 20_000, 2_500, 18_000)) &&
        pieces.Values.Any(piece => InWindow(piece, 20_000, 1_500, 18_000)) &&
        pieces.Values.Any(piece => InWindow(piece, 20_000, 3_100, 18_000)),
        "The Room B window aperture is not physically open between sill and lintel.");
    Expect(pieces[17].Variant == GameWorldStaticVariants.CabinFloorEightByTwelveMeters &&
        WorldStaticPhysicalSemantics.SupportsTop(pieces[17].Flags) &&
        pieces[17].Bounds.MinimumZMillimeters == 9_000 &&
        pieces[17].Bounds.MaximumZExclusiveMillimeters == 21_001 &&
        pieces[17].Bounds.MaximumYExclusiveMillimeters == 1_100,
        "Both rooms require one continuous real, walkable authored floor.");
    Expect(benchmark.FrozenStaticCount == 5,
        "The bookshelf blocker and exterior pine/rock controls are incomplete.");
    var composition = new UltraSimGameComposition(new InventoryLimits(32, 128, 32),
        baseWorldPack: benchmark);
    using var host = new InternalHost(composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_10B9))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0x10B9));
    GamePlayableWorldEntities actor = default;
    Expect(entered.IsEntered &&
        runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out actor),
        "The normal Local authority could not compose the benchmark Pack.");
    host.Tick();
    GameWorldActionStatus roomA = Relocate(16_000, 12_000);
    GameWorldActionStatus doorway = Relocate(16_000, 15_000);
    GameWorldActionStatus roomB = Relocate(16_000, 18_000);
    Expect(roomA == GameWorldActionStatus.Accepted &&
        doorway == GameWorldActionStatus.Accepted &&
        roomB == GameWorldActionStatus.Accepted,
        $"The authored floor/interior doorway does not admit the actor: " +
        $"RoomA={roomA}, doorway={doorway}, RoomB={roomB}.");
    Expect(Relocate(16_000, 12_000) == GameWorldActionStatus.Accepted,
        "The actor could not start an ordinary walk from Room A.");
    var sequencer = new ClientWorldInputSequencer();
    var direct = new PlayableOfflineDirectLink(runtime.Game, actor.PlayerId,
        sequencer.ControlSession);
    Expect(sequencer.TryCreateSemanticCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.CreateDirect(0, GamePlayerCommand.MaximumAxisMagnitude),
            GamePlayerActions.None, true), out GamePlayerCommand walk) &&
        direct.ApplyCommand(walk).WasAdmitted,
        "The benchmark could not admit an ordinary Local movement command.");
    for (int step = 0; step < 50; step++) host.Tick();
    WorldPosition walked = direct.ReadPosition();
    Expect(walked.GlobalZMillimeters > 15_500 && walked.YMillimeters == 1_100,
        $"The actor could not walk across the open interior doorway on the floor: {walked}.");
    Expect(sequencer.TryCreateSemanticCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
            out GamePlayerCommand stop) && direct.ApplyCommand(stop).WasAdmitted,
        "The benchmark could not stop ordinary Local movement.");
    Expect(Relocate(20_000, 12_000) == GameWorldActionStatus.Blocked &&
        Relocate(18_500, 18_000) == GameWorldActionStatus.Blocked,
        "The windowless wall or bookshelf blocker is not authoritative collision.");
    return new TestMetrics(benchmark.Fingerprint.ToString(), 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "Separate v1 WorldPack; two rooms; no Room-A window; one physical Room-B window; " +
        "open interior doorway and actual walk; authored floor; bookshelf blocker; exact Classic fingerprint retained.",
        WorldPackBytes: benchmark.Length);

    GameWorldActionStatus Relocate(long x, long z)
    {
        Expect(WorldPosition.TryFromGlobalMillimeters(x, 1_100, z,
            out WorldPosition position), "The benchmark probe position was invalid.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
            GameWorldActionStatus>((world, _, game) => game.WorldActions.RelocateActor(
                world, game.WorldState, ResolveInWorld(world, actor.PlayerId),
                position, out WorldPosition _), out GameWorldActionStatus status),
            "The benchmark authority was unavailable.");
        return status;
    }
}

static TestMetrics PlayableAuthorityBlocksCabinDoorWallsAndChest()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1050))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0x1050));
    Expect(entered.IsEntered, "The collision actor could not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
        (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
        out GamePlayableWorldEntities entities),
        "The collision fixture could not populate.");
    host.Tick();

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, input.ControlSession);

    Send(GameLocomotionUpdate.CreateDirect(0, GamePlayerCommand.MaximumAxisMagnitude));
    for (int step = 0; step < 30; step++) host.Tick();
    WorldPosition closedDoorStop = link.ReadPosition();
    Expect(closedDoorStop.GlobalXMillimeters == GamePlayableWorld.DoorPosition.GlobalXMillimeters - 500 &&
        closedDoorStop.GlobalZMillimeters == 8_500 && ReadActiveLocomotionCount(runtime.Game) == 1,
        $"The closed cabin door did not block retained Direct: {closedDoorStop}.");

    GamePlayerCommandResult opened = Send(
        GameLocomotionUpdate.Unchanged,
        GamePlayerActions.Interact,
        entities.CabinDoorId);
    Expect(opened.Interaction == GameWorldActionStatus.Accepted && opened.DoorIsOpen,
        "The blocked actor could not open the nearby cabin door.");
    for (int step = 0; step < 12; step++) host.Tick();
    WorldPosition throughDoor = link.ReadPosition();
    Expect(throughDoor.GlobalZMillimeters > 9_100,
        $"The open cabin door remained an authoritative blocker: {throughDoor}.");

    Stop();
    Relocate(16_000, 12_000);
    Send(GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MinimumAxisMagnitude, 0));
    for (int step = 0; step < 50; step++) host.Tick();
    WorldPosition wallStop = link.ReadPosition();
    Expect(wallStop.GlobalXMillimeters == 12_500 &&
        wallStop.GlobalZMillimeters == 12_000 && ReadActiveLocomotionCount(runtime.Game) == 1,
        $"The west cabin Multi wall did not block retained Direct: {wallStop}.");

    Stop();
    Relocate(16_000, 12_000);
    Send(GameLocomotionUpdate.CreateDestination(10_000, 12_000));
    for (int step = 0; step < 60 && ReadActiveLocomotionCount(runtime.Game) != 0; step++)
        host.Tick();
    WorldPosition destinationStop = link.ReadPosition();
    Expect(destinationStop.GlobalXMillimeters == 12_500 &&
        ReadActiveLocomotionCount(runtime.Game) == 0,
        $"A blocked straight-line Destination did not stop at the cabin wall: {destinationStop}.");

    Relocate(18_000, 10_500);
    Send(GameLocomotionUpdate.CreateDirect(0, GamePlayerCommand.MaximumAxisMagnitude));
    for (int step = 0; step < 30; step++) host.Tick();
    WorldPosition chestStop = link.ReadPosition();
    Expect(chestStop.GlobalXMillimeters == 18_000 &&
        chestStop.GlobalZMillimeters == 11_200,
        $"The public cabin chest did not block retained Direct: {chestStop}.");

    return new TestMetrics(
        HashText($"{closedDoorStop}|{throughDoor}|{wallStop}|{destinationStop}|{chestStop}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "Authority collision uses prepared static/Multi bounds plus concrete closed-door/chest bounds; open door passes, Direct remains held, blocked straight Destination retires.",
        InputCommands: input.Snapshot().CommandsProduced);

    GamePlayerCommandResult Send(
        in GameLocomotionUpdate locomotion,
        GamePlayerActions held = GamePlayerActions.None,
        PersistentEntityId target = default)
    {
        Expect(input.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(locomotion, held, true),
                out GamePlayerCommand command),
            $"The collision {locomotion.Kind} command could not be formed.");
        GamePlayerCommandResult result = link.ApplyCommand(command, target);
        Expect(result.WasAdmitted &&
            result.Locomotion is GameWorldActionStatus.Accepted or GameWorldActionStatus.NotRequested,
            $"The collision {locomotion.Kind} command was not admitted.");
        return result;
    }

    void Stop()
    {
        GamePlayerCommandResult stopped = Send(GameLocomotionUpdate.Stop);
        Expect(stopped.Locomotion == GameWorldActionStatus.Accepted &&
            ReadActiveLocomotionCount(runtime.Game) == 0,
            "The collision actor could not stop retained locomotion.");
    }

    void Relocate(long globalX, long globalZ)
    {
        Expect(WorldPosition.TryFromGlobalMillimeters(globalX, 1_000, globalZ, out WorldPosition requested) &&
            runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, generationId, game) => game.WorldActions.RelocateActor(
                    world,
                    game.WorldState,
                    ResolveInWorld(world, entities.PlayerId),
                    requested,
                    out _),
                out GameWorldActionStatus status) &&
            status == GameWorldActionStatus.Accepted,
            $"The collision actor could not relocate to ({globalX},{globalZ}).");
    }
}

static TestMetrics ConceptDemoWorldCombinesFloatingBaseCaveAndSkyShells()
{
    WorldPack legacy = GamePlayableWorld.CreateWorldPack();
    WorldPack first = GamePlayableWorld.CreateConceptDemoWorldPack();
    WorldPack second = GamePlayableWorld.CreateConceptDemoWorldPack();
    Expect(first.Fingerprint == second.Fingerprint && first.Fingerprint != legacy.Fingerprint,
        "The Concept Demo WorldPack is not deterministic and distinct from the preserved Cabin pack.");
    Expect(legacy.ChunkCount == GamePlayableWorld.ChunkCount &&
        first.ChunkCount == GamePlayableWorld.ConceptChunkCount &&
        first.ChunkCount > legacy.ChunkCount,
        "The enlarged Concept Demo source did not remain distinct from the historical Cabin extent.");
    Expect(first.AdditionalTerrainSurfaceFragmentCount == 3,
        "The Concept Demo pack does not contain exactly Mountain, cave-ledge, and Sky fragments.");

    for (int chunkZ = GamePlayableWorld.FlatTestMinimumChunkZ;
         chunkZ <= GamePlayableWorld.FlatTestMaximumChunkZ;
         chunkZ++)
    for (int chunkX = GamePlayableWorld.FlatTestMinimumChunkX;
         chunkX <= GamePlayableWorld.FlatTestMaximumChunkX;
         chunkX++)
    {
        var key = new SpatialChunkKey(GameWorldState.DefaultSpace, chunkX, chunkZ);
        Expect(first.TryGetBaseTerrain(key, out WorldTerrainChunkView flat) &&
            flat.PresentSurfaceCount == TerrainChunkFormat.MaterialCellCount,
            $"The 3x3 flat movement-test area contains a topology hole in {key}.");
        for (int vertex = 0; vertex < flat.HeightVertexCount; vertex++)
        {
            Expect(flat.GetAbsoluteHeightMillimeters(vertex) ==
                GamePlayableWorld.FlatTestHeightMillimeters,
                $"The 3x3 movement-test area is not flat in {key} at vertex {vertex}.");
        }
    }

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
    Expect(first.TryBind(binding, out BoundWorldPack? bound, out WorldPackBindingError bindError),
        $"The Concept Demo WorldPack did not bind: {bindError}.");
    BoundWorldPack world = bound!;

    Expect(WorldPosition.TryFromGlobalMillimeters(63_000, 0, 60_000, out WorldPosition outer) &&
        world.ProbeBaseTerrainSurface(outer).Status == BaseTerrainSurfaceStatus.Absent,
        "The central terrain does not have a real intentional-void outer edge.");
    Expect(world.ProbeBaseTerrainSurface(GamePlayableWorld.SkyJumpStart).IsPresent &&
        world.ProbeBaseTerrainSurface(GamePlayableWorld.SkyTelepadDestination).Status ==
            BaseTerrainSurfaceStatus.Absent,
        "The detached Sky Island is not separated from the central base sheet by real void.");

    Span<WorldTerrainColumnSurface> surfaces = stackalloc WorldTerrainColumnSurface[8];
    Expect(world.TryCopyTerrainColumnSurfaces(
            GamePlayableWorld.CaveLedgePoint, surfaces, out int caveCount, out _) ==
            WorldTerrainColumnQueryStatus.Complete && caveCount == 5,
        "The mountain/cave column did not expose five actual structural faces.");
    Expect(surfaces[0].SurfaceId == GamePlayableWorld.MountainSurface &&
        surfaces[0].Face == WorldTerrainSurfaceFace.Top &&
        surfaces[0].HeightMillimeters == 10_000 &&
        surfaces[1].SurfaceId == GamePlayableWorld.MountainSurface &&
        surfaces[1].Face == WorldTerrainSurfaceFace.Bottom &&
        surfaces[1].HeightMillimeters == 8_000 &&
        surfaces[2].SurfaceId == GamePlayableWorld.CaveLedgeSurface &&
        surfaces[2].Face == WorldTerrainSurfaceFace.Top &&
        surfaces[2].HeightMillimeters == 4_000 &&
        surfaces[3].SurfaceId == GamePlayableWorld.CaveLedgeSurface &&
        surfaces[3].Face == WorldTerrainSurfaceFace.Bottom &&
        surfaces[3].HeightMillimeters == 3_500 &&
        surfaces[4].IsBase &&
        surfaces[4].HeightMillimeters == GamePlayableWorld.FlatTestHeightMillimeters,
        "The cave surfaces were ordered by something other than physical elevation.");
    Expect(world.TryCopyTerrainColumnSurfaces(
            GamePlayableWorld.SkyTelepadDestination, surfaces, out int skyCount, out _) ==
            WorldTerrainColumnQueryStatus.Complete && skyCount == 2 &&
        surfaces[0].SurfaceId == GamePlayableWorld.SkyIslandSurface &&
        surfaces[0].HeightMillimeters == 3_500 &&
        surfaces[1].SurfaceId == GamePlayableWorld.SkyIslandSurface &&
        surfaces[1].Face == WorldTerrainSurfaceFace.Bottom,
        "The Sky Island did not remain one detached two-sided terrain shell.");

    return new TestMetrics(
        first.Fingerprint.ToString(),
        0, 0, 0, 0, 0, first.Length, 0, 0, 0, 0, 0,
        "D0.0 deterministically composes intentional BaseTerrain void, a complete 3x3 flat movement-test area, preserved cave base, thick Mountain/ledge shells, and a detached Sky Island without changing the historical Cabin pack.",
        WorldPackBytes: first.Length);
}

static TestMetrics ConceptDemoTelepadUsesAuthoritativeDestination()
{
    ConceptDemoStructuralSource source = GamePlayableWorld.CreateConceptDemoStructuralSource();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        conceptDemoStructuralSource: source);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_D000))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0xD000));
    Expect(entered.IsEntered, "The Concept Demo participant did not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.PopulateConceptDemo(world, game, entered.Entity),
            out GamePlayableWorldEntities entities) && entities.SkyTelepadId.IsValid,
        "The Concept Demo dynamic population did not include its telepad.");
    host.Tick();

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(runtime.Game, entities.PlayerId, input.ControlSession);
    Expect(input.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true),
            out GamePlayerCommand distantUse),
        "The distant telepad command could not be formed.");
    WorldPosition before = link.ReadPosition();
    GamePlayerCommandResult distant = link.ApplyCommand(distantUse, entities.SkyTelepadId);
    Expect(distant.Interaction == GameWorldActionStatus.OutOfRange && link.ReadPosition() == before,
        "An out-of-range telepad interaction relocated the actor.");

    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, generationId, game) => game.WorldActions.RelocateActor(
                world,
                game.WorldState,
                ResolveInWorld(world, entities.PlayerId),
                GamePlayableWorld.SkyTelepadPosition,
                out _),
            out GameWorldActionStatus approach) && approach == GameWorldActionStatus.Accepted,
        "The actor could not approach the authored telepad.");
    var nextInput = new ClientWorldInputSequencer(input.ControlSession, distantUse.Sequence + 1);
    Expect(nextInput.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true),
            out GamePlayerCommand use),
        "The accepted telepad command could not be formed.");
    GamePlayerCommandResult accepted = link.ApplyCommand(use, entities.SkyTelepadId);
    WorldPosition arrived = link.ReadPosition();
    Expect(accepted.Interaction == GameWorldActionStatus.Accepted &&
        arrived == GamePlayableWorld.SkyTelepadDestination,
        "The telepad did not use its exact Game-owned occupied Sky Island destination.");

    var forged = new GamePlayerCommand(
        input.ControlSession,
        use.Sequence + 1,
        GameLocomotionUpdate.Stop,
        GamePlayerActions.Interact,
        GamePlayerActions.Interact);
    GamePlayerCommandResult wrongTarget = link.ApplyCommand(forged, entities.WoodlandAnimalId);
    Expect(wrongTarget.Interaction == GameWorldActionStatus.MissingTarget &&
        link.ReadPosition() == GamePlayableWorld.SkyTelepadDestination,
        "A non-telepad public target acquired telepad semantics.");

    return new TestMetrics(
        HashText($"{before}|{arrived}|{distant.Interaction}|{wrongTarget.Interaction}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "The public telepad accepts only an in-range authoritative target, obtains no destination from the client, and relocates through the existing occupancy-aware Game primitive.",
        InputCommands: nextInput.Snapshot().CommandsProduced + input.Snapshot().CommandsProduced + 1);
}

static TestMetrics ConceptDemoFarGateUsesSparseAuthoredGeography()
{
    ConceptDemoStructuralSource source = GamePlayableWorld.CreateConceptDemoStructuralSource();
    PreparedWorldComposition authored = source.Prepare(ConceptDemoStructuralSource.CreateBinding());
    var centralKey = new SpatialChunkKey(
        GameWorldState.DefaultSpace,
        GamePlayableWorld.CentralGateDestination.Chunk);
    var farKey = new SpatialChunkKey(
        GameWorldState.DefaultSpace,
        GamePlayableWorld.FarGateDestination.Chunk);
    var gapKey = new SpatialChunkKey(
        GameWorldState.DefaultSpace,
        (centralKey.Coord.X + farKey.Coord.X) / 2,
        centralKey.Coord.Z);
    Expect(authored.TryGetBaseTerrain(centralKey, out _) &&
        authored.TryGetBaseTerrain(farKey, out _) &&
        !authored.TryGetBaseTerrain(gapKey, out _),
        "The Far Island fixture did not preserve two authored clusters separated by genuinely unauthored geography.");
    Expect(GamePlayableWorld.FarGateDestination.GlobalXMillimeters -
        GamePlayableWorld.CentralGateDestination.GlobalXMillimeters >= 2_000_000,
        "The Far Island fixture did not establish a meaningful long-distance relocation.");

    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        conceptDemoStructuralSource: source);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_D002))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0xD002));
    Expect(entered.IsEntered, "The Far Island participant did not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.PopulateConceptDemo(world, game, entered.Entity),
            out GamePlayableWorldEntities entities) &&
        entities.CentralFarGateId.IsValid && entities.FarReturnGateId.IsValid,
        "The Concept Demo population did not contain both reciprocal Far Gates.");
    host.Tick();

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(runtime.Game, entities.PlayerId, input.ControlSession);
    Expect(input.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true),
            out GamePlayerCommand distantUse),
        "The distant Far Gate command could not be formed.");
    WorldPosition before = link.ReadPosition();
    GamePlayerCommandResult distant = link.ApplyCommand(distantUse, entities.CentralFarGateId);
    Expect(distant.Interaction == GameWorldActionStatus.OutOfRange && link.ReadPosition() == before,
        "An out-of-range Far Gate interaction relocated the actor.");

    Relocate(GamePlayableWorld.CentralFarGatePosition);
    var centralInput = new ClientWorldInputSequencer(input.ControlSession, distantUse.Sequence + 1);
    Expect(centralInput.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true),
            out GamePlayerCommand useCentral),
        "The central Far Gate command could not be formed.");
    GamePlayerCommandResult outbound = link.ApplyCommand(useCentral, entities.CentralFarGateId);
    Expect(outbound.Interaction == GameWorldActionStatus.Accepted &&
        link.ReadPosition() == GamePlayableWorld.FarGateDestination,
        "The central Far Gate did not use its exact Game-owned Far Island destination.");

    // Cross the actual authored 127/128 Pack seam under ordinary direct locomotion.
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
        (world, _, game) =>
        {
            long globalX = 128L * GameWorldSpatialProfile.WorldChunkEdgeUnits + 500;
            foreach (long globalZ in new long[] { 8_000, 12_000, 24_000, -8_000 })
            {
                if (!WorldPosition.TryFromGlobalMillimeters(globalX, 0, globalZ,
                        out WorldPosition groundProbe) ||
                    !game.WorldState.TrySampleBaseTerrainHeight(groundProbe, out int height) ||
                    !WorldPosition.TryFromGlobalMillimeters(globalX, height, globalZ,
                        out WorldPosition candidate))
                    continue;
                if (game.WorldActions.RelocateActor(world, game.WorldState,
                        ResolveInWorld(world, entities.PlayerId), candidate, out WorldPosition _) ==
                    GameWorldActionStatus.Accepted)
                    return true;
            }
            return false;
        }, out bool seamStart) && seamStart,
        "The actual Far Island seam had no safe movement start.");
    var seamInput = new ClientWorldInputSequencer(input.ControlSession, useCentral.Sequence + 1);
    Expect(seamInput.TryCreateSemanticCommand(new ClientWorldCommandIntent(
        GameLocomotionUpdate.CreateDirect(
            -GamePlayerCommand.MaximumAxisMagnitude, 0), GamePlayerActions.None, true),
        out GamePlayerCommand westward), "Far Island westward command could not form.");
    Expect(link.ApplyCommand(westward).Locomotion == GameWorldActionStatus.Accepted,
        "Far Island westward command was not accepted.");
    for (int ticks = 0; ticks < 250 && link.ReadPosition().Chunk.X == 128; ticks++) host.Tick();
    Expect(link.ReadPosition().Chunk.X == 127,
        "Ordinary movement did not cross the Far Island 128→127 Pack seam.");
    Expect(seamInput.TryCreateSemanticCommand(new ClientWorldCommandIntent(
        GameLocomotionUpdate.CreateDirect(
            GamePlayerCommand.MaximumAxisMagnitude, 0), GamePlayerActions.None, true),
        out GamePlayerCommand eastward), "Far Island eastward command could not form.");
    Expect(link.ApplyCommand(eastward).Locomotion == GameWorldActionStatus.Accepted,
        "Far Island eastward command was not accepted.");
    for (int ticks = 0; ticks < 250 && link.ReadPosition().Chunk.X == 127; ticks++) host.Tick();
    Expect(link.ReadPosition().Chunk.X == 128,
        "Ordinary movement did not cross the Far Island 127→128 Pack seam.");

    Relocate(GamePlayableWorld.FarReturnGatePosition);
    var returnInput = new ClientWorldInputSequencer(input.ControlSession, eastward.Sequence + 1);
    Expect(returnInput.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true),
            out GamePlayerCommand useReturn),
        "The return Far Gate command could not be formed.");
    GamePlayerCommandResult inbound = link.ApplyCommand(useReturn, entities.FarReturnGateId);
    Expect(inbound.Interaction == GameWorldActionStatus.Accepted &&
        link.ReadPosition() == GamePlayableWorld.CentralGateDestination,
        "The Far Island return gate did not use its exact Game-owned central destination.");

    return new TestMetrics(
        HashText($"{source.Manifest.Fingerprint}|{centralKey}|{farKey}|{gapKey}|{outbound.Interaction}|{inbound.Interaction}"),
        0, 0, 0, 0, 0, source.West.Length + source.East.Length, 0, 0, 0, 0, 0,
        "D0.2 uses two sparse authored clusters with no gap chunks and reciprocal authoritative gates whose destinations never come from the client.",
        InputCommands: input.Snapshot().CommandsProduced +
            centralInput.Snapshot().CommandsProduced + seamInput.Snapshot().CommandsProduced +
            returnInput.Snapshot().CommandsProduced,
        ChunkCrossings: 2);

    void Relocate(in WorldPosition destination)
    {
        WorldPosition target = destination;
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, generationId, game) => game.WorldActions.RelocateActor(
                    world,
                    game.WorldState,
                    ResolveInWorld(world, entities.PlayerId),
                    target,
                    out _),
                out GameWorldActionStatus status) && status == GameWorldActionStatus.Accepted,
            $"The actor could not approach Far Gate at {target}.");
    }
}

static TestMetrics ConceptDemoInformationalSignValidatesAuthoritativeTargetAndRange()
{
    WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_D003))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0xD003));
    Expect(entered.IsEntered, "The informational-sign participant did not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.PopulateConceptDemo(world, game, entered.Entity),
            out GamePlayableWorldEntities entities) && entities.WelcomeSignId.IsValid,
        "The Concept Demo population did not contain its welcome sign.");
    host.Tick();

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(runtime.Game, entities.PlayerId, input.ControlSession);
    Expect(input.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true),
            out GamePlayerCommand use),
        "The informational-sign command could not be formed.");
    GamePlayerCommandResult accepted = link.ApplyCommand(use, entities.WelcomeSignId);
    Expect(accepted.Interaction == GameWorldActionStatus.Accepted,
        "The nearby authoritative informational sign did not admit interaction.");

    var wrongTarget = new GamePlayerCommand(
        input.ControlSession,
        use.Sequence + 1,
        GameLocomotionUpdate.Stop,
        GamePlayerActions.Interact,
        GamePlayerActions.Interact);
    GamePlayerCommandResult wrong = link.ApplyCommand(wrongTarget, entities.WoodlandAnimalId);
    Expect(wrong.Interaction == GameWorldActionStatus.MissingTarget,
        "A non-sign public object acquired informational-sign semantics.");

    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, generationId, game) => game.WorldActions.RelocateActor(
                world,
                game.WorldState,
                ResolveInWorld(world, entities.PlayerId),
                GamePlayableWorld.CentralFarGatePosition,
                out _),
            out GameWorldActionStatus relocation) && relocation == GameWorldActionStatus.Accepted,
        "The informational-sign range test could not relocate its actor.");
    var distantCommand = wrongTarget with { Sequence = wrongTarget.Sequence + 1 };
    GamePlayerCommandResult distant = link.ApplyCommand(distantCommand, entities.WelcomeSignId);
    Expect(distant.Interaction == GameWorldActionStatus.OutOfRange,
        "A distant informational sign opened without authoritative range admission.");

    return new TestMetrics(
        HashText($"{entities.WelcomeSignId.Value}|{accepted.Interaction}|" +
            $"{wrong.Interaction}|{distant.Interaction}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "The Concept Demo sign is a real public Game target; only its trusted identity and range admit interaction, while UI text remains outside client intent.",
        InputCommands: 3);
}

static TestMetrics ConceptDemoFallbackLabKeepsPresentationFailureOutOfAuthority()
{
    WorldPack first = GamePlayableWorld.CreateConceptDemoWorldPack();
    WorldPack second = GamePlayableWorld.CreateConceptDemoWorldPack();
    Expect(first.Fingerprint == second.Fingerprint && first.MultiInstanceCount == 3,
        "The Fallback Lab WorldPack was not canonical or did not retain its authored Multi identity.");
    Expect(first.TryGetMultiInstance(new MultiInstanceId(3), out WorldMultiInstanceRecord lab) &&
        lab.PieceCount == 8,
        "The Fallback Lab did not retain four pedestal/object pairs under one stable Multi identity.");

    WorldPosition[] positions =
    [
        GamePlayableWorld.FallbackLabCorrectPosition,
        GamePlayableWorld.FallbackLabModelMissingPosition,
        GamePlayableWorld.FallbackLabTextureMissingPosition,
        GamePlayableWorld.FallbackLabBothMissingPosition,
    ];
    ushort[] variants =
    [
        GameWorldStaticVariants.FallbackLabCorrect,
        GameWorldStaticVariants.FallbackLabModelMissing,
        GameWorldStaticVariants.FallbackLabTextureMissing,
        GameWorldStaticVariants.FallbackLabModelAndTextureMissing,
    ];
    Span<WorldStaticQueryHit> hits = stackalloc WorldStaticQueryHit[16];
    WorldStaticBounds canaryBounds = default;
    for (int index = 0; index < positions.Length; index++)
    {
        WorldPosition position = positions[index];
        var query = new WorldStaticBounds(
            position.GlobalXMillimeters - 1_100,
            position.YMillimeters,
            position.GlobalZMillimeters - 1_100,
            position.GlobalXMillimeters + 1_101,
            position.YMillimeters + 2_100,
            position.GlobalZMillimeters + 1_101);
        Expect(first.TryQueryStaticBounds(
                query, 9, hits, out int count, out _, out WorldStaticQueryError error),
            $"The Fallback Lab authority query failed: {error}.");
        WorldStaticQueryHit canary = default;
        int labPieces = 0;
        for (int hit = 0; hit < count; hit++)
        {
            if (hits[hit].MultiId != new MultiInstanceId(3)) continue;
            labPieces++;
            if (hits[hit].Variant == variants[index]) canary = hits[hit];
        }
        Expect(labPieces >= 2 && canary.Kind == WorldStaticQueryKind.MultiFragment &&
            canary.DefinitionId == new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Rock) &&
            canary.Flags == (WorldStaticFlags.Renderable | WorldStaticFlags.Collidable),
            $"Fallback Lab canary {variants[index]} lost canonical authority geometry.");
        if (index == 0)
            canaryBounds = canary.Bounds;
        else
            Expect(
                canary.Bounds.MaximumXExclusiveMillimeters - canary.Bounds.MinimumXMillimeters ==
                    canaryBounds.MaximumXExclusiveMillimeters - canaryBounds.MinimumXMillimeters &&
                canary.Bounds.MaximumYExclusiveMillimeters - canary.Bounds.MinimumYMillimeters ==
                    canaryBounds.MaximumYExclusiveMillimeters - canaryBounds.MinimumYMillimeters &&
                canary.Bounds.MaximumZExclusiveMillimeters - canary.Bounds.MinimumZMillimeters ==
                    canaryBounds.MaximumZExclusiveMillimeters - canaryBounds.MinimumZMillimeters,
                "A presentation-failure class changed the canary's authoritative collision size.");
    }

    return new TestMetrics(
        HashText($"{first.Fingerprint}|{lab.Id.Value}|{lab.PieceCount}|" +
            string.Join(',', variants)),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "D0.4 keeps correct/model-missing/texture-missing/both-missing canaries in one immutable " +
        "World Multi with identical authoritative collision semantics; only Godot presentation degrades.");
}

static TestMetrics SimplePhysicsGenerationReplacement()
{
    const ulong persistentWorld = 0x80_1075;
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(persistentWorld))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;

    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, UltraSimSimplePhysics>(
            (_, _, game) => game.SimplePhysics,
            out UltraSimSimplePhysics? initialPhysics),
        "The initial generation had no Simple Physics capability.");
    Expect(!typeof(UltraSimSimplePhysics).Assembly.GetReferencedAssemblies().Any(
            static assembly => assembly.Name == "UltraSim.Game.Composition"),
        "The Simple Physics project depends on playable Game composition meaning.");

    GameStaging staging = composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(persistentWorld)));
    ComponentLookup<GameCabinDoorState> doorLookup =
        staging.World.PrepareLookup<GameCabinDoorState>();
    GameGenerationState? candidate = null;
    bool published = false;
    PersistentEntityId playerId = default;
    try
    {
        staging.World.Seal();
        staging.World.Initialize();
        staging.World.BeginRun();
        Entity player = Entity.Null;
        Entity door = Entity.Null;
        Expect(WorldPosition.TryFromGlobalMillimeters(
                16_000, 1_000, 8_500, out WorldPosition playerStart) &&
            staging.World.TryInstantiate(staging.Content.Character, out player) &&
            staging.WorldState.TrySetPosition(staging.World, player, playerStart) &&
            staging.World.TryInstantiate(staging.Content.CabinDoor, out door) &&
            staging.WorldState.TrySetPosition(
                staging.World, door, GamePlayableWorld.DoorPosition) &&
            staging.World.TryWrite(
                door, doorLookup, new GameCabinDoorState { IsOpen = 1 }) &&
            staging.WorldState.TryReadPublic(
                staging.World, door, out WorldPublicDescriptor doorPublic) &&
            staging.WorldState.TrySetPublic(
                staging.World,
                door,
                doorPublic with { Orientation = WorldYaw.QuarterTurn }),
            "The replacement generation's canonical open-door state could not be prepared.");
        playerId = staging.World.GetOrAssignPersistentId(player);

        candidate = new GameGenerationState(
            new AccountingStore(),
            staging.Inventory,
            staging.Items,
            staging.Content,
            staging.WorldState,
            staging.SimulationLookup,
            staging.Replication,
            staging.WorldActions);
        Expect(runtime.Game.TryPublishPreparedGeneration(
                staging.World, candidate, out _),
            "The open-door replacement generation could not publish.");
        published = true;
        candidate = null;
    }
    finally
    {
        if (!published)
        {
            candidate?.Dispose();
            staging.World.Dispose();
        }
    }

    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, UltraSimSimplePhysics>(
            (_, _, game) => game.SimplePhysics,
            out UltraSimSimplePhysics? replacementPhysics) &&
        !ReferenceEquals(initialPhysics, replacementPhysics),
        "Generation replacement reused the prior Simple Physics capability.");

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, playerId, input.ControlSession);
    Expect(input.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDirect(
                    0, GamePlayerCommand.MaximumAxisMagnitude),
                GamePlayerActions.None,
                true),
            out GamePlayerCommand direct) &&
        link.ApplyCommand(direct).Locomotion == GameWorldActionStatus.Accepted,
        "The replacement generation could not retain northward Direct motion.");
    for (int step = 0; step < 8; step++)
        host.Tick();
    WorldPosition throughOpenDoor = link.ReadPosition();
    Expect(throughOpenDoor.GlobalZMillimeters > 9_100,
        $"The rebuilt open-door projection remained physically blocking: {throughOpenDoor}.");

    return new TestMetrics(
        HashText($"{initialPhysics!.ReservedBytes}:{replacementPhysics!.ReservedBytes}:" +
            throughOpenDoor),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "A replacement generation owns fresh Simple Physics/projection state rebuilt from canonical open-door ECS truth; the Physics project has no Composition dependency.",
        InputCommands: input.Snapshot().CommandsProduced);
}

static TestMetrics SimplePhysicsOccupancy()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1080))));
    host.Start();

    Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<
            IGameGenerationStateProvider, long>(
            (_, _, game) =>
            {
                var body = new SimplePhysicsBody(300, 300, 150, 150, 1_800, 500, 500);
                Expect(!default(SimpleOccupancyResult).CanOccupy,
                    "A default occupancy result was interpreted as clear.");
                bool invalidRejected = false;
                try
                {
                    _ = game.SimplePhysics.ProbeOccupancy(
                        default, ReadOnlySpan<WorldStaticBounds>.Empty, true);
                }
                catch (ArgumentException)
                {
                    invalidRejected = true;
                }
                Expect(invalidRejected, "A default occupancy request did not fail closed.");

                var clearRequest = new SimpleOccupancyRequest(
                    GamePlayableWorld.PlayerStart, body);
                SimpleOccupancyResult clear = game.SimplePhysics.ProbeOccupancy(
                    clearRequest, ReadOnlySpan<WorldStaticBounds>.Empty, true);
                Expect(clear.CanOccupy &&
                    clear.Position.YMillimeters == GamePlayableWorld.PlayerStart.YMillimeters,
                    $"A clear first-class occupancy probe did not resolve canonical terrain: {clear}.");

                SimpleMotionResult sameEndpoint = game.SimplePhysics.ResolveMotion(
                    new SimpleMotionRequest(
                        GamePlayableWorld.PlayerStart,
                        GamePlayableWorld.PlayerStart.GlobalXMillimeters,
                        GamePlayableWorld.PlayerStart.GlobalZMillimeters,
                        body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                Expect(sameEndpoint.WasResolved && sameEndpoint.Position == clear.Position,
                    "Motion and first-class occupancy disagreed on the same clear endpoint.");

                Expect(WorldPosition.TryFromGlobalMillimeters(
                        16_000, 1_000, 15_000, out WorldPosition wallTarget),
                    "The static-wall occupancy target could not be represented.");
                SimpleOccupancyResult staticBlocked = game.SimplePhysics.ProbeOccupancy(
                    new SimpleOccupancyRequest(wallTarget, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                Expect(staticBlocked.Status == SimpleOccupancyStatus.Blocked,
                    "A collidable prepared cabin wall was occupiable.");

                WorldPosition clearPosition = clear.Position;
                WorldStaticBounds projectedBlocker = new(
                    clearPosition.GlobalXMillimeters - 100,
                    clearPosition.YMillimeters,
                    clearPosition.GlobalZMillimeters - 100,
                    clearPosition.GlobalXMillimeters + 101,
                    clearPosition.YMillimeters + 1_000,
                    clearPosition.GlobalZMillimeters + 101);
                ReadOnlySpan<WorldStaticBounds> oneDynamicBlocker = [projectedBlocker];
                Expect(game.SimplePhysics.ProbeOccupancy(
                        clearRequest, oneDynamicBlocker, true).Status ==
                    SimpleOccupancyStatus.Blocked,
                    "An intersecting projected dynamic bound was occupiable.");
                Expect(game.SimplePhysics.ProbeOccupancy(
                        clearRequest, ReadOnlySpan<WorldStaticBounds>.Empty, false).Status ==
                    SimpleOccupancyStatus.Blocked,
                    "An incomplete dynamic projection was interpreted as clear.");

                Expect(WorldPosition.TryFromGlobalMillimeters(
                        1_000_000, 0, 1_000_000, out WorldPosition unprepared),
                    "The outside-prepared-world occupancy target could not be represented.");
                Expect(game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(unprepared, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true).Status == SimpleOccupancyStatus.OutsidePreparedWorld,
                    "An unprepared occupancy target was not distinguished from physical blockage.");

                for (int index = 0; index < 16; index++)
                    _ = game.SimplePhysics.ProbeOccupancy(
                        clearRequest, ReadOnlySpan<WorldStaticBounds>.Empty, true);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 1_000; index++)
                {
                    SimpleOccupancyResult result = game.SimplePhysics.ProbeOccupancy(
                        clearRequest, ReadOnlySpan<WorldStaticBounds>.Empty, true);
                    if (!result.CanOccupy)
                        throw new InvalidOperationException("The warmed occupancy target changed.");
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;
            },
            out long allocated) && allocated == 0,
        $"Warmed first-class occupancy allocated {allocated} caller bytes.");

    return new TestMetrics(
        HashText("first-class-occupancy-clear-blocked-outside"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, allocated,
        "First-class occupancy shares motion endpoint legality, distinguishes prepared-world failure, fails closed on static/dynamic pressure, and allocates 0 B warmed.");
}

static TestMetrics SimplePhysicsSweptMotion()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1081))));
    host.Start();

    Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<
            IGameGenerationStateProvider, long>(
            (_, _, game) =>
            {
                var body = new SimplePhysicsBody(300, 300, 150, 150, 1_800, 500, 500);
                WorldPosition staticStart = At(13_500, 7_000);
                WorldPosition staticTarget = At(13_500, 11_000);
                Expect(game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(staticTarget, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true).CanOccupy,
                    "The thin-static sweep target was not independently clear.");
                Expect(Move(staticStart, staticTarget).Status == SimpleMotionStatus.Blocked,
                    "A 4 m request tunneled through the 300 mm cabin wall.");

                WorldPosition cornerOutside = At(11_000, 8_000);
                WorldPosition cornerInside = At(13_000, 10_000);
                Expect(game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(cornerOutside, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true).CanOccupy &&
                    game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(cornerInside, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true).CanOccupy,
                    "The diagonal-corner sweep endpoints were not independently clear.");
                Expect(Move(cornerOutside, cornerInside).Status == SimpleMotionStatus.Blocked &&
                    Move(cornerInside, cornerOutside).Status == SimpleMotionStatus.Blocked,
                    "Diagonal motion cut through the cabin corner in one direction.");

                WorldPosition dynamicStart = At(13_000, 7_000);
                WorldPosition dynamicTarget = At(19_000, 7_000);
                long minimumY = Math.Min(
                    dynamicStart.YMillimeters, dynamicTarget.YMillimeters) - 100L;
                long maximumY = Math.Max(
                    dynamicStart.YMillimeters, dynamicTarget.YMillimeters) +
                    body.HeightMillimeters + 100L;
                var thinDynamic = new WorldStaticBounds(
                    15_999, minimumY, 6_500,
                    16_001, maximumY, 7_500);
                ReadOnlySpan<WorldStaticBounds> thinDynamicSpan = [thinDynamic];
                Expect(game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(dynamicTarget, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true).CanOccupy,
                    "The thin-dynamic sweep target was not independently clear.");
                Expect(Move(dynamicStart, dynamicTarget, thinDynamicSpan).Status ==
                        SimpleMotionStatus.Blocked &&
                    Move(dynamicTarget, dynamicStart, thinDynamicSpan).Status ==
                        SimpleMotionStatus.Blocked,
                    "A request tunneled through a 2 mm projected dynamic blocker.");

                var exactTouch = new WorldStaticBounds(
                    15_999, minimumY, 7_301,
                    16_001, maximumY, 7_500);
                ReadOnlySpan<WorldStaticBounds> exactTouchSpan = [exactTouch];
                Expect(Move(dynamicStart, dynamicTarget, exactTouchSpan).WasResolved,
                    "Half-open swept bounds treated exact boundary contact as overlap.");

                WorldPosition diagonalStart = At(13_000, 7_000);
                WorldPosition diagonalTarget = At(19_000, 8_000);
                var offPath = new WorldStaticBounds(
                    12_900, minimumY, 7_900,
                    13_100, maximumY, 8_100);
                ReadOnlySpan<WorldStaticBounds> offPathSpan = [offPath];
                Expect(Move(diagonalStart, diagonalTarget, offPathSpan).WasResolved,
                    "The swept broadphase rectangle became a false physical collision.");

                WorldPosition capacityStart = At(-30_000, 7_000);
                WorldPosition capacityTarget = At(30_000, 7_000);
                Expect(Move(capacityStart, capacityTarget).Status == SimpleMotionStatus.Blocked,
                    "A sweep exceeding bounded static candidate coverage did not fail closed.");

                WorldPosition warmTarget = At(14_000, 7_000);
                for (int index = 0; index < 16; index++)
                    _ = Move(dynamicStart, warmTarget);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 1_000; index++)
                {
                    if (!Move(dynamicStart, warmTarget).WasResolved)
                        throw new InvalidOperationException("The warmed clear sweep changed.");
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;

                WorldPosition At(long x, long z)
                {
                    if (!WorldPosition.TryFromGlobalMillimeters(x, 0, z, out WorldPosition seed) ||
                        !game.WorldState.TrySampleBaseTerrainHeight(seed, out int y) ||
                        !WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition position))
                    {
                        throw new InvalidOperationException(
                            $"The swept-motion fixture position {x}/{z} was not prepared.");
                    }
                    return position;
                }

                SimpleMotionResult Move(
                    in WorldPosition current,
                    in WorldPosition target,
                    ReadOnlySpan<WorldStaticBounds> dynamicBounds = default) =>
                    game.SimplePhysics.ResolveMotion(
                        new SimpleMotionRequest(
                            current,
                            target.GlobalXMillimeters,
                            target.GlobalZMillimeters,
                            body),
                        dynamicBounds,
                        true);
            },
            out long allocated) && allocated == 0,
        $"Warmed swept Simple Physics allocated {allocated} caller bytes.");

    return new TestMetrics(
        HashText("swept-static-dynamic-corner-exact"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, allocated,
        "Exact integer swept-AABB motion blocks thin static/dynamic tunneling and diagonal corners, avoids broadphase false positives, fails closed at capacity, and allocates 0 B warmed.");
}

static TestMetrics SimplePhysicsTerrainShellSupport()
{
    WorldPack pack = CreatePhysicsTerrainShellWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1083))));
    host.Start();
    UltraSimSimplePhysics? initialPhysics = null;

    Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<
            IGameGenerationStateProvider, long>(
            (world, generationId, game) =>
            {
                initialPhysics = game.SimplePhysics;
                var body = new SimplePhysicsBody(300, 300, 150, 150, 1_800, 500, 500);
                Expect(!default(SimpleMotionResult).WasResolved &&
                    !default(SimpleOccupancyResult).CanOccupy,
                    "Default support-aware results did not fail closed.");

                SimpleOccupancyResult mountain = Occupy(3_500, 10_000, 3_500);
                SimpleOccupancyResult middle = Occupy(3_500, 5_000, 3_500);
                SimpleOccupancyResult baseFloor = Occupy(3_500, 0, 3_500);
                SimpleOccupancyResult belowBase = Occupy(3_500, -5_000, 3_500);
                Expect(mountain.CanOccupy && mountain.IsGrounded &&
                    mountain.Position.YMillimeters == 10_000 &&
                    mountain.Support.SurfaceId == new WorldSurfaceId(900) &&
                    middle.CanOccupy && middle.Position.YMillimeters == 5_000 &&
                    middle.Support.SurfaceId == new WorldSurfaceId(1) &&
                    baseFloor.CanOccupy && baseFloor.Position.YMillimeters == 0 &&
                    baseFloor.Support.IsBase &&
                    belowBase.CanOccupy && belowBase.Position.YMillimeters == -5_000 &&
                    belowBase.Support.SurfaceId == new WorldSurfaceId(333),
                    "Occupancy selected a shell by identity/rank instead of requested physical elevation.");

                var tallBody = new SimplePhysicsBody(300, 300, 150, 150, 3_500, 500, 500);
                SimpleOccupancyResult belowMountain = game.SimplePhysics.ProbeOccupancy(
                    new SimpleOccupancyRequest(Position(3_500, 5_000, 3_500), tallBody),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                Expect(belowMountain.Status == SimpleOccupancyStatus.Blocked,
                    "The mountain shell Bottom did not become clearance geometry below its Top support.");

                SimpleOccupancyResult lowCeiling = Occupy(7_000, 0, 3_500);
                Expect(lowCeiling.Status == SimpleOccupancyStatus.Blocked,
                    "A 1.5 m terrain-shell ceiling admitted a 1.8 m body.");
                var exactClearanceBody = new SimplePhysicsBody(300, 300, 150, 150, 1_500, 500, 500);
                Expect(game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(Position(7_000, 0, 3_500), exactClearanceBody),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true).CanOccupy,
                    "Exact half-open contact at body height was treated as ceiling overlap.");
                SimpleOccupancyResult intentionalVoid = Occupy(12_500, 0, 12_500);
                Expect(intentionalVoid.Status == SimpleOccupancyStatus.Unsupported,
                    "Intentional resident BaseTerrain void was not classified as unsupported.");
                SimpleOccupancyResult shellOverVoid = Occupy(15_000, 3_000, 12_000);
                Expect(shellOverVoid.CanOccupy && shellOverVoid.Position.YMillimeters == 3_000 &&
                    shellOverVoid.Support.SurfaceId == new WorldSurfaceId(50),
                    "An additional shell could not independently support a body over BaseTerrain void.");
                Expect(Occupy(1_000_000, 0, 1_000_000).Status ==
                    SimpleOccupancyStatus.OutsidePreparedWorld,
                    "Unavailable structural data was not distinguished from intentional void.");

                WorldPosition lowStepStart = Position(1_500, 0, 8_500);
                SimpleOccupancyResult ambiguousLowStep = Occupy(3_000, 200, 8_500);
                Expect(ambiguousLowStep.Status == SimpleOccupancyStatus.Blocked,
                    "An equidistant occupancy request was silently raised from lower BaseTerrain onto another shell.");
                SimpleOccupancyResult nearestLowStepTop = Occupy(3_000, 300, 8_500);
                Expect(nearestLowStepTop.CanOccupy &&
                    nearestLowStepTop.Position.YMillimeters == 400 &&
                    nearestLowStepTop.Support.SurfaceId == new WorldSurfaceId(700),
                    "Occupancy did not honor the physically nearest terrain-shell support.");
                SimpleMotionResult stepUp = Move(lowStepStart, 3_000, 8_500);
                Expect(stepUp.WasResolved && stepUp.IsGrounded &&
                    stepUp.Position.YMillimeters == 400 &&
                    stepUp.Support.SurfaceId == new WorldSurfaceId(700),
                    $"A reachable 400 mm two-sided terrain step did not resolve onto its Top face: {stepUp}.");
                SimpleMotionResult stepDown = Move(stepUp.Position, 1_500, 8_500);
                Expect(stepDown.WasResolved && stepDown.Position.YMillimeters == 0 &&
                    stepDown.Support.IsBase,
                    "A bounded 400 mm grounded descent did not return to BaseTerrain.");
                WorldPosition cadence20 = TraverseLowStep(20);
                WorldPosition cadence60 = TraverseLowStep(60);
                WorldPosition cadence120 = TraverseLowStep(120);
                Expect(cadence20 == cadence60 && cadence20 == cadence120 &&
                    cadence20 == stepUp.Position,
                    "Terrain step/support selection diverged at 20/60/120 Hz-equivalent sampling.");

                WorldPosition highStepStart = Position(7_500, 0, 8_500);
                Expect(Move(highStepStart, 9_000, 8_500).Status == SimpleMotionStatus.Blocked,
                    "A 1,000 mm closed terrain step exceeded the 500 mm profile but did not block.");

                WorldPosition gapStart = Position(11_500, 0, 12_500);
                SimpleOccupancyResult clearBeyondGap = Occupy(13_500, 0, 12_500);
                Expect(clearBeyondGap.CanOccupy &&
                    Move(gapStart, 13_500, 12_500).Status == SimpleMotionStatus.Unsupported,
                    "Bounded grounded path sampling skipped an intentional one-meter terrain gap.");
                Expect(Move(Position(1_000, 0, 1_000), 130_000, 1_000).Status ==
                    SimpleMotionStatus.Blocked,
                    "Grounded traversal beyond the fixed sample budget did not fail closed.");

                Expect(world.TryInstantiate(game.Content.Character, out Entity actor) &&
                    game.WorldState.TrySetPosition(world, actor, Position(1_000, 0, 1_000)),
                    "The Game-owned terrain-shell actor could not be created.");
                GameWorldActionStatus relocation = game.WorldActions.RelocateActor(
                    world,
                    game.WorldState,
                    actor,
                    Position(3_500, 10_000, 3_500),
                    out WorldPosition relocated);
                Expect(relocation == GameWorldActionStatus.Accepted &&
                    relocated.YMillimeters == 10_000 &&
                    game.WorldState.TryReadPosition(world, actor, out WorldPosition committed) &&
                    committed == relocated,
                    "Game relocation did not commit the selected mountain-shell support.");
                GameWorldActionStatus voidRelocation = game.WorldActions.RelocateActor(
                    world,
                    game.WorldState,
                    actor,
                    Position(12_500, 0, 12_500),
                    out _);
                Expect(voidRelocation == GameWorldActionStatus.Unsupported &&
                    game.WorldState.TryReadPosition(world, actor, out WorldPosition afterRejected) &&
                    afterRejected == relocated,
                    "Unsupported relocation did not preserve authoritative Game position transactionally.");

                WorldPosition warmStart = Position(1_000, 0, 1_000);
                for (int index = 0; index < 16; index++)
                    _ = Move(warmStart, 1_250, 1_000);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 1_000; index++)
                {
                    SimpleMotionResult result = Move(warmStart, 1_250, 1_000);
                    if (!result.WasResolved || result.Position.YMillimeters != 0)
                        throw new InvalidOperationException("The warmed grounded shell query changed.");
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;

                SimpleOccupancyResult Occupy(long x, int y, long z) =>
                    game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(Position(x, y, z), body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);

                SimpleMotionResult Move(in WorldPosition current, long x, long z) =>
                    game.SimplePhysics.ResolveMotion(
                        new SimpleMotionRequest(current, x, z, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);

                WorldPosition TraverseLowStep(int frequency)
                {
                    WorldPosition position = lowStepStart;
                    for (int tick = 1; tick <= frequency; tick++)
                    {
                        long targetX = 1_500 + (1_500L * tick / frequency);
                        SimpleMotionResult result = Move(position, targetX, 8_500);
                        if (!result.WasResolved)
                            throw new InvalidOperationException(
                                $"The {frequency} Hz-equivalent terrain step blocked at tick {tick}: {result.Status}.");
                        position = result.Position;
                    }
                    return position;
                }

                static WorldPosition Position(long x, int y, long z)
                {
                    if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
                        throw new InvalidOperationException("A terrain-shell fixture point was invalid.");
                    return value;
                }
            },
            out long allocated) && allocated == 0,
        $"Warmed support/step/clearance resolution allocated {allocated} caller bytes.");

    GameStaging staging = composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x80_1083)));
    GameGenerationState? candidate = null;
    bool published = false;
    try
    {
        staging.World.Seal();
        staging.World.Initialize();
        staging.World.BeginRun();
        candidate = new GameGenerationState(
            new AccountingStore(),
            staging.Inventory,
            staging.Items,
            staging.Content,
            staging.WorldState,
            staging.SimulationLookup,
            staging.Replication,
            staging.WorldActions);
        Expect(host.RuntimeForTesting.Game.TryPublishPreparedGeneration(
                staging.World, candidate, out _),
            "The terrain-shell replacement generation could not publish.");
        published = true;
        candidate = null;
    }
    finally
    {
        if (!published)
        {
            candidate?.Dispose();
            staging.World.Dispose();
        }
    }
    Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<
            IGameGenerationStateProvider, bool>(
            (_, _, game) =>
            {
                var body = new SimplePhysicsBody(300, 300, 150, 150, 1_800, 500, 500);
                if (!WorldPosition.TryFromGlobalMillimeters(
                        3_500, 10_000, 3_500, out WorldPosition target))
                    return false;
                SimpleOccupancyResult result = game.SimplePhysics.ProbeOccupancy(
                    new SimpleOccupancyRequest(target, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                return !ReferenceEquals(initialPhysics, game.SimplePhysics) &&
                    result.CanOccupy && result.Position.YMillimeters == 10_000 &&
                    result.Support.SurfaceId == new WorldSurfaceId(900);
            },
            out bool replacementGrounded) && replacementGrounded,
        "Generation replacement did not rebuild support-aware Physics over the immutable shell pack.");

    return new TestMetrics(
        HashText("terrain-shell-support-step-clearance-void"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, allocated,
        "Geometry-ordered Base/Top support, Bottom clearance, bounded step up/down, intentional void, unavailable data, fixed path-work limits, and 0 B warmed allocation.");
}

static TestMetrics SimplePhysicsAirborneTerrainShellContacts()
{
    WorldPack pack = CreatePhysicsTerrainShellWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1084))));
    host.Start();

    Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<
            IGameGenerationStateProvider, long>(
            (world, generationId, game) =>
            {
                var body = new SimplePhysicsBody(300, 300, 150, 150, 1_800, 500, 500);
                Expect(!default(SimpleAirborneMotionResult).WasResolved,
                    "A default airborne result did not fail closed.");

                SimpleAirborneMotionResult mountain = Air(3_500, 12_000, 3_500, 3_500, 9_000, 3_500);
                SimpleAirborneMotionResult middle = Air(3_500, 7_000, 3_500, 3_500, 4_000, 3_500);
                SimpleAirborneMotionResult ceiling = Air(3_500, 5_000, 3_500, 3_500, 7_000, 3_500);
                SimpleAirborneMotionResult overVoid = Air(12_500, 3_000, 12_500, 12_500, 0, 12_500);
                SimpleAirborneMotionResult acrossVoid = Air(11_500, 1_000, 12_500, 13_500, 1_000, 12_500);
                SimpleAirborneMotionResult unavailable = Air(
                    15_500, 3_000, 3_500, 17_000, 2_000, 3_500);

                Expect(mountain.IsGrounded && mountain.Position.YMillimeters == 10_000 &&
                    mountain.Contact.SurfaceId == new WorldSurfaceId(900),
                    "Descending motion did not land on the highest crossed mountain Top.");
                Expect(middle.IsGrounded && middle.Position.YMillimeters == 5_000 &&
                    middle.Contact.SurfaceId == new WorldSurfaceId(1),
                    "Descending motion did not land on the actual intermediate cave floor.");
                Expect(ceiling.Status == SimpleAirborneMotionStatus.CeilingBlocked &&
                    ceiling.Position.YMillimeters == 6_200 &&
                    ceiling.Contact.Face == WorldTerrainSurfaceFace.Bottom &&
                    ceiling.Contact.SurfaceId == new WorldSurfaceId(900),
                    "Ascending motion did not stop the body at the mountain-shell Bottom face.");
                Expect(overVoid.Status == SimpleAirborneMotionStatus.Airborne &&
                    overVoid.Position.YMillimeters == 0,
                    "Intentional resident void incorrectly became an airborne landing surface.");
                Expect(acrossVoid.Status == SimpleAirborneMotionStatus.Airborne &&
                    acrossVoid.Position.GlobalXMillimeters == 13_500,
                    "Airborne horizontal movement was snapped onto or blocked by a terrain gap.");
                Expect(unavailable.Status == SimpleAirborneMotionStatus.OutsidePreparedWorld,
                    "Unavailable structural data was interpreted as clear airborne space.");

                for (int index = 0; index < 16; index++)
                    _ = Air(12_500, 3_000, 12_500, 12_500, 2_900, 12_500);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 1_000; index++)
                {
                    SimpleAirborneMotionResult result =
                        Air(12_500, 3_000, 12_500, 12_500, 2_900, 12_500);
                    if (result.Status != SimpleAirborneMotionStatus.Airborne)
                        throw new InvalidOperationException("The warmed airborne result changed.");
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;

                SimpleAirborneMotionResult Air(
                    long fromX, int fromY, long fromZ,
                    long toX, int toY, long toZ) =>
                    game.SimplePhysics.ResolveAirborneMotion(
                        new SimpleAirborneMotionRequest(
                            Position(fromX, fromY, fromZ), toX, toY, toZ, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);

                static WorldPosition Position(long x, int y, long z)
                {
                    if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
                        throw new InvalidOperationException("An airborne fixture point was invalid.");
                    return value;
                }
            },
            out long allocated) && allocated == 0,
        $"Warmed airborne terrain-shell resolution allocated {allocated} caller bytes.");

    return new TestMetrics(
        HashText("airborne-top-bottom-contact-void-horizontal"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, allocated,
        "Bounded airborne motion lands on geometry-ordered Top faces, stops at Bottom ceilings, crosses intentional void horizontally, fails closed for unavailable structure, and allocates 0 B warmed.");
}

static TestMetrics SimplePhysicsStaticSupport()
{
    WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1086))));
    host.Start();

    Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<
            IGameGenerationStateProvider, long>(
            (_, _, game) =>
            {
                var body = new SimplePhysicsBody(300, 300, 150, 150, 1_800, 500, 500);
                Expect(WorldPosition.TryFromGlobalMillimeters(
                        5_000, 0, 13_000, out WorldPosition rockColumn),
                    "The authored Concept Demo rock column was invalid.");
                WorldPreparedView prepared = game.WorldState.PreparedWorld ??
                    throw new InvalidOperationException(
                        "The Concept Demo prepared world was unavailable.");
                int terrainY = 0;
                Expect(prepared.TrySampleBaseTerrainHeight(rockColumn, out terrainY),
                    "The authored Concept Demo rock column was unavailable.");
                int rockTopY = checked(terrainY + 900);
                WorldPosition above = Position(5_000, rockTopY + 1_000, 13_000);

                SimpleAirborneMotionResult landing = game.SimplePhysics.ResolveAirborneMotion(
                    new SimpleAirborneMotionRequest(
                        above, 5_000, rockTopY - 500, 13_000, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                Expect(landing.IsGrounded &&
                    landing.Position.YMillimeters == rockTopY &&
                    landing.Contact.Kind == SimplePhysicalSupportKind.StaticGeometry,
                    $"Descending motion did not land on the authored rock top: {landing}.");

                SimpleOccupancyResult occupancy = game.SimplePhysics.ProbeOccupancy(
                    new SimpleOccupancyRequest(landing.Position, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                SimpleMotionResult groundedMove = game.SimplePhysics.ResolveMotion(
                    new SimpleMotionRequest(landing.Position, 5_100, 13_000, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                Expect(occupancy.IsGrounded &&
                    occupancy.Support.Kind == SimplePhysicalSupportKind.StaticGeometry &&
                    groundedMove.IsGrounded &&
                    groundedMove.Position.YMillimeters == rockTopY &&
                    groundedMove.Support.Kind == SimplePhysicalSupportKind.StaticGeometry,
                    "A landed actor could not remain grounded and move across the rock top.");

                // Human play found the exact state where the independent support core has left
                // the rock but the full body still overlaps its top edge. Descending AABB sweep
                // must not leave that airborne actor permanently fixed in place.
                WorldPosition lastRockSupport = Position(5_350, rockTopY, 13_000);
                SimpleAirborneMotionResult departingRock =
                    game.SimplePhysics.ResolveAirborneMotion(
                        new SimpleAirborneMotionRequest(
                            lastRockSupport, 5_417, rockTopY - 1, 13_000, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);
                SimpleAirborneMotionResult rockEdgeEscape =
                    game.SimplePhysics.ResolveAirborneMotion(
                        new SimpleAirborneMotionRequest(
                            departingRock.Position,
                            departingRock.Position.GlobalXMillimeters,
                            departingRock.Position.YMillimeters - 4,
                            departingRock.Position.GlobalZMillimeters,
                            body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);
                SimpleAirborneMotionResult afterRockEdge =
                    game.SimplePhysics.ResolveAirborneMotion(
                        new SimpleAirborneMotionRequest(
                            rockEdgeEscape.Position,
                            rockEdgeEscape.Position.GlobalXMillimeters,
                            rockEdgeEscape.Position.YMillimeters - 10,
                            rockEdgeEscape.Position.GlobalZMillimeters,
                            body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);
                Expect(departingRock.WasResolved &&
                    departingRock.Position.GlobalXMillimeters >
                        lastRockSupport.GlobalXMillimeters &&
                    rockEdgeEscape.WasResolved &&
                    rockEdgeEscape.Position.GlobalXMillimeters >
                        departingRock.Position.GlobalXMillimeters &&
                    afterRockEdge.WasResolved &&
                    afterRockEdge.Position.YMillimeters <
                        rockEdgeEscape.Position.YMillimeters,
                    "An unsupported airborne body remained wedged forever against its former " +
                    "static top edge.");

                WorldPosition wallColumn = Position(16_000, 0, 15_000);
                Expect(prepared.TrySampleBaseTerrainHeight(wallColumn, out int wallTerrainY),
                    "The authored Cabin wall column was unavailable.");
                int wallTopY = checked(wallTerrainY + 2_500);
                SimpleAirborneMotionResult wallLanding = game.SimplePhysics.ResolveAirborneMotion(
                    new SimpleAirborneMotionRequest(
                        Position(16_000, wallTopY + 1_000, 15_000),
                        16_000, wallTopY - 500, 15_000, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                SimpleMotionResult alongWall = game.SimplePhysics.ResolveMotion(
                    new SimpleMotionRequest(wallLanding.Position, 16_500, 15_000, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                SimpleOccupancyResult wallEdge = game.SimplePhysics.ProbeOccupancy(
                    new SimpleOccupancyRequest(Position(19_850, wallTopY, 15_000), body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                SimpleMotionResult offWall = game.SimplePhysics.ResolveMotion(
                    new SimpleMotionRequest(wallEdge.Position, 20_150, 15_000, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                SimpleAirborneMotionResult wallJumpClearance =
                    game.SimplePhysics.ResolveAirborneMotion(
                        new SimpleAirborneMotionRequest(
                            wallLanding.Position, 16_000, wallTopY + 500, 15_000, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);
                Expect(wallLanding.IsGrounded && alongWall.IsGrounded && wallEdge.IsGrounded &&
                    offWall.Status == SimpleMotionStatus.Unsupported &&
                    wallJumpClearance.Status == SimpleAirborneMotionStatus.Airborne,
                    "The smaller support core did not permit stable wall-top landing, traversal, " +
                    "walk-off, and upward launch semantics.");

                WorldPosition shelfColumn = Position(18_500, 0, 14_000);
                Expect(prepared.TrySampleBaseTerrainHeight(shelfColumn, out int shelfTerrainY),
                    "The authored Cabin shelf column was unavailable.");
                int shelfTopY = checked(shelfTerrainY + 1_800);
                SimpleAirborneMotionResult shelfLanding = game.SimplePhysics.ResolveAirborneMotion(
                    new SimpleAirborneMotionRequest(
                        Position(18_500, shelfTopY + 1_000, 14_000),
                        18_500, shelfTopY - 500, 14_000, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                Expect(shelfLanding.IsGrounded,
                    "A static top wider than the support core but narrower than the body did not support landing.");

                WorldPosition treeFace = Position(
                    3_951, GamePlayableWorld.FlatTestHeightMillimeters + 61, 3_000);
                SimpleAirborneMotionResult treeContact =
                    game.SimplePhysics.ResolveAirborneMotion(
                        new SimpleAirborneMotionRequest(
                            treeFace,
                            3_851,
                            GamePlayableWorld.FlatTestHeightMillimeters - 651,
                            3_000,
                            body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);
                Expect(treeContact.Status == SimpleAirborneMotionStatus.Landed &&
                    treeContact.Position.GlobalXMillimeters == treeFace.GlobalXMillimeters &&
                    treeContact.Position.YMillimeters ==
                        GamePlayableWorld.FlatTestHeightMillimeters,
                    $"A combined wall/landing contact did not preserve its clear vertical " +
                    $"component: {treeContact}.");

                for (int index = 0; index < 16; index++)
                {
                    _ = game.SimplePhysics.ResolveMotion(
                        new SimpleMotionRequest(landing.Position, 5_100, 13_000, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);
                    _ = game.SimplePhysics.ResolveAirborneMotion(
                        new SimpleAirborneMotionRequest(
                            lastRockSupport, 5_417, rockTopY - 1, 13_000, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);
                    _ = game.SimplePhysics.ResolveAirborneMotion(
                        new SimpleAirborneMotionRequest(
                            treeFace,
                            3_851,
                            GamePlayableWorld.FlatTestHeightMillimeters - 651,
                            3_000,
                            body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 1_000; index++)
                {
                    SimpleMotionResult result = game.SimplePhysics.ResolveMotion(
                        new SimpleMotionRequest(landing.Position, 5_100, 13_000, body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);
                    if (!result.IsGrounded)
                        throw new InvalidOperationException("The warmed rock-support result changed.");
                    SimpleAirborneMotionResult edge =
                        game.SimplePhysics.ResolveAirborneMotion(
                            new SimpleAirborneMotionRequest(
                                lastRockSupport, 5_417, rockTopY - 1, 13_000, body),
                            ReadOnlySpan<WorldStaticBounds>.Empty,
                            true);
                    if (!edge.WasResolved ||
                        edge.Position.GlobalXMillimeters <=
                            lastRockSupport.GlobalXMillimeters)
                        throw new InvalidOperationException(
                            "The warmed static-edge escape result changed.");
                    SimpleAirborneMotionResult wall =
                        game.SimplePhysics.ResolveAirborneMotion(
                            new SimpleAirborneMotionRequest(
                                treeFace,
                                3_851,
                                GamePlayableWorld.FlatTestHeightMillimeters - 651,
                                3_000,
                                body),
                            ReadOnlySpan<WorldStaticBounds>.Empty,
                            true);
                    if (!wall.IsGrounded ||
                        wall.Position.GlobalXMillimeters != treeFace.GlobalXMillimeters)
                        throw new InvalidOperationException(
                            "The warmed airborne-wall decomposition result changed.");
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;

                static WorldPosition Position(long x, int y, long z)
                {
                    if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
                        throw new InvalidOperationException("A rock-support fixture point was invalid.");
                    return value;
                }
            },
            out long allocated) && allocated == 0,
        $"Warmed static-support resolution allocated {allocated} caller bytes.");

    return new TestMetrics(
        HashText("static-top-support-core-wall-shelf-grounded-movement"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, allocated,
        "Ordinary collidable rock, thin-wall, and shelf tops support airborne landing and " +
        "grounded traversal through the independent feet footprint; unsupported rock-edge " +
        "contact escapes, combined wall/landing contact preserves vertical progress, and the " +
        "paths allocate 0 B warmed.");
}

static TestMetrics SimplePhysicsStaticTopPolicy()
{
    WorldPack pack = CreateStaticTopPolicyWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1087))));
    host.Start();

    Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<
            IGameGenerationStateProvider, bool>(
            (_, _, game) =>
            {
                var body = new SimplePhysicsBody(300, 300, 150, 150, 1_800, 500, 500);
                Expect(body.IsValid &&
                    !new SimplePhysicsBody(300, 300, 301, 150, 1_800, 500, 500).IsValid,
                    "The support footprint was not bounded by the collision footprint.");

                SimpleOccupancyResult tiny = Occupy(4_000, 1_000, 4_000);
                SimpleOccupancyResult optedOut = Occupy(8_000, 1_000, 8_000);
                SimpleMotionResult blockedByOptedOut = game.SimplePhysics.ResolveMotion(
                    new SimpleMotionRequest(Position(6_500, 0, 8_000), 9_500, 8_000, body),
                    ReadOnlySpan<WorldStaticBounds>.Empty,
                    true);
                Expect(!tiny.IsGrounded && tiny.Status == SimpleOccupancyStatus.Blocked,
                    "A top smaller than the support core established standing support.");
                Expect(!optedOut.IsGrounded && optedOut.Status == SimpleOccupancyStatus.Blocked &&
                    blockedByOptedOut.Status == SimpleMotionStatus.Blocked,
                    "NoTopSupport stopped blocking the body or incorrectly established standing support.");
                return true;

                SimpleOccupancyResult Occupy(long x, int y, long z) =>
                    game.SimplePhysics.ProbeOccupancy(
                        new SimpleOccupancyRequest(Position(x, y, z), body),
                        ReadOnlySpan<WorldStaticBounds>.Empty,
                        true);

                static WorldPosition Position(long x, int y, long z)
                {
                    if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
                        throw new InvalidOperationException("A static-top-policy point was invalid.");
                    return value;
                }
            },
            out bool passed) && passed,
        "The static top-support policy fixture failed.");

    return new TestMetrics(
        HashText("static-blocking-support-core-no-top-support"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "A tiny top cannot ground the normal body; NoTopSupport remains body-blocking but cannot " +
        "establish support. No sliding/contact response is claimed.");
}

static TestMetrics AuthoritativeJumpFallLandingAndRecovery()
{
    WorldPack pack = CreatePhysicsTerrainShellWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack,
        verticalMotionOptions: new GameVerticalMotionOptions(
            JumpSpeedMillimetersPerSecond: 11_000,
            GravityMillimetersPerSecondSquared: -18_000,
            RecoveryMinimumYMillimeters: -8_000,
            RecoveryPosition: GamePlayableWorld.PlayerStart));
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1085))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(1085));
    Expect(entered.IsEntered, "The vertical-motion actor did not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
        "The vertical-motion fixture could not populate.");
    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(runtime.Game, entities.PlayerId, input.ControlSession);
    WorldPosition verticalStart = Position(
        GamePlayableWorld.PlayerStart.GlobalXMillimeters,
        0,
        6_000);
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, generationId, game) => game.WorldActions.RelocateActor(
                world, game.WorldState, ResolveInWorld(world, entities.PlayerId),
                verticalStart, out WorldPosition _), out GameWorldActionStatus startRelocation) &&
        startRelocation == GameWorldActionStatus.Accepted,
        "The vertical-motion actor could not enter the shell fixture's BaseTerrain.");

    Expect(input.TryCreateCommand(new ClientWorldInputSample(
            -1, 0, GamePlayerActions.Jump, true), out GamePlayerCommand jump),
        "The jump edge could not be formed.");
    GamePlayerCommandResult admitted = link.ApplyCommand(jump);
    Expect(admitted.Jump == GameWorldActionStatus.Accepted,
        $"The grounded jump was not admitted: {admitted.Jump}.");
    host.Tick();
    WorldPosition afterFirst = link.ReadPosition();
    Expect(afterFirst.GlobalXMillimeters < verticalStart.GlobalXMillimeters &&
        afterFirst.YMillimeters > verticalStart.YMillimeters &&
        ReadVertical(runtime.Game, entities.PlayerId).IsAirborne,
        "One authoritative jump step did not compose horizontal and upward motion.");
    WorldYaw takeoffFacing = ReadFacing();
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, _, game) => game.WorldActions.FaceActorToward(
                world,
                game.WorldState,
                ResolveInWorld(world, entities.PlayerId),
                afterFirst.GlobalXMillimeters + 10_000,
                afterFirst.GlobalZMillimeters,
                out WorldYaw ignored),
            out GameWorldActionStatus airborneFacing) &&
        airborneFacing == GameWorldActionStatus.Accepted &&
        ReadFacing() == takeoffFacing,
        "Live directional facing rotated the authoritative body while airborne.");

    var hostileDoubleJump = new GamePlayerCommand(
        input.ControlSession,
        jump.Sequence + 1,
        GameLocomotionUpdate.Unchanged,
        GamePlayerActions.Jump,
        GamePlayerActions.Jump);
    GamePlayerCommandResult duplicateAirJump = link.ApplyCommand(hostileDoubleJump);
    Expect(duplicateAirJump.Jump == GameWorldActionStatus.NotGrounded,
        "A fresh hostile Jump edge created an airborne double-jump.");

    Expect(input.TryCreateCommand(new ClientWorldInputSample(
            1, 0, GamePlayerActions.None, true), out GamePlayerCommand reverse),
        "The jump fixture could not request a mid-air direction reversal.");
    // The hostile sequence consumed sequence 2, so use an explicitly fresh reversal.
    reverse = reverse with { Sequence = hostileDoubleJump.Sequence + 1 };
    GamePlayerCommandResult reverseResult = link.ApplyCommand(reverse);
    Expect(reverseResult.WasAdmitted &&
        reverseResult.Locomotion == GameWorldActionStatus.Accepted,
        $"The mid-air correction probe was not admitted: {reverseResult}.");
    WorldPosition afterCorrectionAttempt = afterFirst;
    for (int tick = 0; tick < 8 && afterCorrectionAttempt == afterFirst; tick++)
    {
        host.Tick(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
        afterCorrectionAttempt = link.ReadPosition();
    }
    Expect(afterCorrectionAttempt.GlobalXMillimeters < afterFirst.GlobalXMillimeters,
        "Live Direct input altered the authoritative horizontal trajectory after takeoff: " +
        $"first={afterFirst}, correction={afterCorrectionAttempt}, " +
        $"vertical={ReadVertical(runtime.Game, entities.PlayerId)}.");

    Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.None, true), out GamePlayerCommand stop),
        "The jump fixture could not release post-landing horizontal input.");
    stop = stop with { Sequence = reverse.Sequence + 1 };
    _ = link.ApplyCommand(stop);
    int landingBudget = 180;
    while (ReadVertical(runtime.Game, entities.PlayerId).IsAirborne && landingBudget-- > 0)
        host.Tick();
    GameVerticalMotionSnapshot landed = ReadVertical(runtime.Game, entities.PlayerId);
    WorldPosition landing = link.ReadPosition();
    Expect(landingBudget >= 0 && landed.IsGrounded && landing.YMillimeters == 0 &&
        landed.CumulativeJumpsAccepted == 1 && landed.CumulativeLandings == 1,
        "The authoritative jump did not fall and land exactly on BaseTerrain: " +
        $"budget={landingBudget}, state={landed}, position={landing}.");

    WorldPosition gapStart = Position(9_500, 0, 15_000);
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, generationId, game) => game.WorldActions.RelocateActor(
                world, game.WorldState, ResolveInWorld(world, entities.PlayerId),
                gapStart, out WorldPosition _), out GameWorldActionStatus relocation) &&
        relocation == GameWorldActionStatus.Accepted,
        "The recovery fixture could not relocate beside intentional void.");
    var fallSession = new ClientWorldInputSequencer(input.ControlSession, hostileDoubleJump.Sequence + 3);
    Expect(fallSession.TryCreateCommand(new ClientWorldInputSample(
            1, 0, GamePlayerActions.None, true), out GamePlayerCommand walkOff),
        "The walk-off command could not be formed.");
    _ = link.ApplyCommand(walkOff);
    int airborneBudget = 20;
    while (!ReadVertical(runtime.Game, entities.PlayerId).IsAirborne && airborneBudget-- > 0)
        host.Tick();
    Expect(airborneBudget >= 0 && link.ReadPosition().GlobalXMillimeters > gapStart.GlobalXMillimeters,
        "Grounded locomotion did not transition into horizontal airborne motion over void.");
    Expect(fallSession.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.None, true), out GamePlayerCommand stopFall),
        "The falling actor could not release horizontal input.");
    _ = link.ApplyCommand(stopFall);
    long recoveriesBefore = ReadVertical(runtime.Game, entities.PlayerId).CumulativeRecoveries;
    int recoveryBudget = 300;
    while (ReadVertical(runtime.Game, entities.PlayerId).CumulativeRecoveries == recoveriesBefore &&
           recoveryBudget-- > 0)
        host.Tick();
    WorldPosition recovered = link.ReadPosition();
    GameVerticalMotionSnapshot recovery = ReadVertical(runtime.Game, entities.PlayerId);
    Expect(recoveryBudget >= 0 && recovery.IsGrounded &&
        recovery.CumulativeRecoveries == recoveriesBefore + 1 &&
        recovered.GlobalXMillimeters == GamePlayableWorld.PlayerStart.GlobalXMillimeters &&
        recovered.GlobalZMillimeters == GamePlayableWorld.PlayerStart.GlobalZMillimeters &&
        recovered.YMillimeters == 0,
        "Game-owned fall recovery did not use the occupancy-aware safe relocation point: " +
        $"budget={recoveryBudget}, before={recoveriesBefore}, state={recovery}, " +
        $"position={recovered}.");

    WorldPosition cadence20 = RunJumpCadence(20, 0x80_1086);
    WorldPosition cadence60 = RunJumpCadence(60, 0x80_1087);
    WorldPosition cadence120 = RunJumpCadence(120, 0x80_1088);
    Expect(cadence20.YMillimeters == 0 && cadence20 == cadence60 && cadence20 == cadence120,
        "Jump/fall/landing final truth diverged across 20/60/120 Hz.");

    return new TestMetrics(
        HashText($"{afterFirst}|{afterCorrectionAttempt}|{landing}|{recovered}|{cadence20}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "Game admits one grounded Jump edge, captures takeoff direction without live air steering, integrates exact fixed-point gravity, lands on terrain, rejects double-jump, recovers below the configured fall envelope, and converges at 20/60/120 Hz.");

    WorldYaw ReadFacing() => runtime.Game.TryUseActiveGeneration<
        IGameGenerationStateProvider, WorldYaw>(
        (world, _, game) => game.WorldState.TryReadPublic(
            world,
            ResolveInWorld(world, entities.PlayerId),
            out WorldPublicDescriptor descriptor)
                ? descriptor.Orientation
                : throw new InvalidOperationException("The jump actor public descriptor was missing."),
        out WorldYaw facing)
            ? facing
            : throw new InvalidOperationException("The jump actor generation was unavailable.");

    static GameVerticalMotionSnapshot ReadVertical(ServerGameFacet gameFacet, PersistentEntityId id)
    {
        Expect(gameFacet.TryUseActiveGeneration<IGameGenerationStateProvider, GameVerticalMotionSnapshot>(
                (world, _, game) =>
                {
                    Expect(game.WorldActions.TryReadVerticalMotion(
                            world, ResolveInWorld(world, id), out GameVerticalMotionSnapshot state),
                        "Vertical state was unavailable.");
                    return state;
                }, out GameVerticalMotionSnapshot snapshot),
            "Vertical-state read did not run.");
        return snapshot;
    }

    static WorldPosition RunJumpCadence(int frequency, ulong worldId)
    {
        WorldPack cadencePack = CreatePhysicsTerrainShellWorldPack();
        var cadenceComposition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32), baseWorldPack: cadencePack);
        using var cadenceHost = new InternalHost(
            cadenceComposition,
            RuntimeOptions(
                new PersistenceIdentityOptions(new PersistentWorldId(worldId)),
                frequencyHz: frequency));
        cadenceHost.Start();
        using var cadenceAdmission = new AdmissionService(
            cadenceHost.RuntimeForTesting.Game, cadenceComposition);
        AdmissionResult cadenceEntered = cadenceAdmission.JoinAccountless(
            new ParticipantId(checked((ulong)(2_000 + frequency))));
        Expect(cadenceEntered.IsEntered, $"The {frequency} Hz jump actor did not enter.");
        Expect(cadenceHost.RuntimeForTesting.Game.TryUseActiveGeneration<
                IGameGenerationStateProvider, GamePlayableWorldEntities>(
                (world, _, game) => GamePlayableWorld.Populate(world, game, cadenceEntered.Entity),
                out GamePlayableWorldEntities cadenceEntities),
            $"The {frequency} Hz jump fixture could not populate.");
        var cadenceInput = new ClientWorldInputSequencer();
        var cadenceLink = new PlayableOfflineDirectLink(
            cadenceHost.RuntimeForTesting.Game,
            cadenceEntities.PlayerId,
            cadenceInput.ControlSession);
        WorldPosition cadenceStart = Position(
            GamePlayableWorld.PlayerStart.GlobalXMillimeters,
            0,
            GamePlayableWorld.PlayerStart.GlobalZMillimeters);
        Expect(cadenceHost.RuntimeForTesting.Game.TryUseActiveGeneration<
                IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, generationId, game) => game.WorldActions.RelocateActor(
                    world, game.WorldState, ResolveInWorld(world, cadenceEntities.PlayerId),
                    cadenceStart, out WorldPosition _), out GameWorldActionStatus relocated) &&
            relocated == GameWorldActionStatus.Accepted,
            $"The {frequency} Hz actor could not enter BaseTerrain.");
        Expect(cadenceInput.TryCreateCommand(new ClientWorldInputSample(
                0, 0, GamePlayerActions.Jump, true), out GamePlayerCommand command) &&
            cadenceLink.ApplyCommand(command).Jump == GameWorldActionStatus.Accepted,
            $"The {frequency} Hz jump was not accepted.");
        bool observedAirborne = false;
        for (int tick = 0; tick < frequency * 3; tick++)
        {
            cadenceHost.Tick();
            GameVerticalMotionSnapshot state = ReadVertical(
                cadenceHost.RuntimeForTesting.Game, cadenceEntities.PlayerId);
            observedAirborne |= state.IsAirborne;
            if (observedAirborne && state.IsGrounded)
                return cadenceLink.ReadPosition();
        }
        throw new InvalidOperationException($"The {frequency} Hz jump did not land.");
    }

    static WorldPosition Position(long x, int y, long z)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
            throw new InvalidOperationException("A vertical Game fixture point was invalid.");
        return value;
    }
}

static TestMetrics AuthoritativeAirborneWallContactKeepsGravityProgress()
{
    WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_108C))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(108_012));
    Expect(entered.IsEntered, "The airborne-wall actor did not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.PopulateConceptDemo(
                world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
        "The airborne-wall fixture could not populate.");

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, input.ControlSession);
    // The authored tree centered at (3000,3000) ends at X=3651. With the 300 mm body
    // half-extent, X=3951 is exact non-overlapping contact with its east face.
    WorldPosition contact = Position(
        3_951,
        GamePlayableWorld.FlatTestHeightMillimeters,
        3_000);
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, generationId, game) => game.WorldActions.RelocateActor(
                world,
                game.WorldState,
                ResolveInWorld(world, entities.PlayerId),
                contact,
                out WorldPosition _),
            out GameWorldActionStatus relocated) &&
        relocated == GameWorldActionStatus.Accepted,
        "The airborne-wall actor could not reach exact tree-face contact.");

    Expect(input.TryCreateCommand(new ClientWorldInputSample(
            -1, 0, GamePlayerActions.Jump, true), out GamePlayerCommand jump) &&
        link.ApplyCommand(jump).Jump == GameWorldActionStatus.Accepted,
        "The tree-contact jump was not admitted.");

    bool observedAirborne = false;
    int stationaryTicks = 0;
    int maximumStationaryTicks = 0;
    int maximumDownwardStep = 0;
    WorldPosition previous = contact;
    int budget = 180;
    while (budget-- > 0)
    {
        host.Tick(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
        WorldPosition current = link.ReadPosition();
        GameVerticalMotionSnapshot vertical = ReadVertical();
        observedAirborne |= vertical.IsAirborne;
        if (current == previous)
            stationaryTicks++;
        else
            stationaryTicks = 0;
        maximumStationaryTicks = Math.Max(maximumStationaryTicks, stationaryTicks);
        if (current.YMillimeters < previous.YMillimeters)
            maximumDownwardStep = Math.Max(
                maximumDownwardStep,
                previous.YMillimeters - current.YMillimeters);
        Expect(current.GlobalXMillimeters >= contact.GlobalXMillimeters,
            "Airborne wall contact penetrated the authored tree.");
        previous = current;
        if (observedAirborne && vertical.IsGrounded)
            break;
    }

    GameVerticalMotionSnapshot landed = ReadVertical();
    WorldPosition final = link.ReadPosition();
    Expect(budget >= 0 && observedAirborne && landed.IsGrounded &&
        landed.CumulativeLandings == 1 && landed.CumulativeRecoveries == 0 &&
        final.YMillimeters == GamePlayableWorld.FlatTestHeightMillimeters &&
        maximumStationaryTicks <= 2 && maximumDownwardStep <= 500,
        "A jump held into a tree suspended, snapped, recovered, or failed to land normally: " +
        $"budget={budget}, observedAirborne={observedAirborne}, state={landed}, " +
        $"position={final}, stationary={maximumStationaryTicks}, " +
        $"downwardStep={maximumDownwardStep}.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, long>(
            (world, generationId, game) => game.WorldActions.ReservedBytes,
            out long worldActionReservedBytes),
        "The airborne-wall fixture could not read its bounded Game action reservation.");

    return new TestMetrics(
        HashText($"{contact}|{final}|{maximumStationaryTicks}|{maximumDownwardStep}"),
        0, 0, 0, 0, 0, pack.Length, 0, 0, 0, 0, 0,
        "A captured jump trajectory held into exact tree-face contact preserves bounded vertical " +
        "progress every tick and lands without penetration, accumulated-gravity snap, or recovery.",
        WorldPackBytes: pack.Length,
        WorldActionReservedBytes: worldActionReservedBytes);

    GameVerticalMotionSnapshot ReadVertical()
    {
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameVerticalMotionSnapshot>(
                (world, _, game) =>
                {
                    Expect(game.WorldActions.TryReadVerticalMotion(
                            world,
                            ResolveInWorld(world, entities.PlayerId),
                            out GameVerticalMotionSnapshot state),
                        "The airborne-wall vertical state was unavailable.");
                    return state;
                },
                out GameVerticalMotionSnapshot snapshot),
            "The airborne-wall vertical-state read did not run.");
        return snapshot;
    }

    static WorldPosition Position(long x, int y, long z)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
            throw new InvalidOperationException("An airborne-wall fixture point was invalid.");
        return value;
    }
}

static TestMetrics AuthoritativeAirbornePreparedBoundaryKeepsGravityProgress()
{
    WorldPack pack = CreatePhysicsTerrainShellWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_108D))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(108_013));
    Expect(entered.IsEntered, "The prepared-boundary actor did not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.PopulateConceptDemo(
                world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
        "The prepared-boundary fixture could not populate.");

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, input.ControlSession);
    // The synthetic pack owns only chunk (0,0), whose east edge is 16,000 mm. This start keeps
    // the complete 600 mm body inside, while the first eastward airborne step asks for a footprint
    // beyond prepared structure. Vertical motion at the retained X/Z remains fully valid.
    WorldPosition edge = Position(15_650, 0, 8_000);
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, _, game) => game.WorldActions.RelocateActor(
                world,
                game.WorldState,
                ResolveInWorld(world, entities.PlayerId),
                edge,
                out WorldPosition _),
            out GameWorldActionStatus relocated) &&
        relocated == GameWorldActionStatus.Accepted,
        "The prepared-boundary actor could not reach the exact edge fixture.");
    Expect(input.TryCreateCommand(new ClientWorldInputSample(
            1, 0, GamePlayerActions.Jump, true), out GamePlayerCommand jump) &&
        link.ApplyCommand(jump).Jump == GameWorldActionStatus.Accepted,
        "The prepared-boundary jump was not admitted.");

    int maximumY = edge.YMillimeters;
    bool observedAirborne = false;
    bool landed = false;
    for (int tick = 0; tick < 240; tick++)
    {
        host.Tick(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
        WorldPosition current = link.ReadPosition();
        maximumY = Math.Max(maximumY, current.YMillimeters);
        Expect(current.GlobalXMillimeters <= edge.GlobalXMillimeters,
            "An outside prepared horizontal target was committed.");
        GameVerticalMotionSnapshot vertical = ReadVertical();
        observedAirborne |= vertical.IsAirborne;
        if (observedAirborne && vertical.IsGrounded)
        {
            landed = true;
            break;
        }
    }

    WorldPosition final = link.ReadPosition();
    GameVerticalMotionSnapshot finalVertical = ReadVertical();
    Expect(observedAirborne && maximumY > edge.YMillimeters && landed &&
        finalVertical.IsGrounded && final == edge &&
        finalVertical.CumulativeLandings == 1 && finalVertical.CumulativeRecoveries == 0,
        "Prepared-boundary horizontal rejection suspended gravity, forced recovery, or changed " +
        $"the accepted position: maxY={maximumY}, final={final}, vertical={finalVertical}.");

    return new TestMetrics(
        HashText($"{edge}|{maximumY}|{final}|{finalVertical.CumulativeLandings}"),
        0, 0, 0, 0, 0, pack.Length, 0, 0, 0, 0, 0,
        "An airborne trajectory aimed beyond prepared structure preserves its independently valid " +
        "vertical component and lands without fixed-point accumulation or recovery.",
        WorldPackBytes: pack.Length);

    GameVerticalMotionSnapshot ReadVertical()
    {
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameVerticalMotionSnapshot>(
                (world, _, game) =>
                {
                    Expect(game.WorldActions.TryReadVerticalMotion(
                            world,
                            ResolveInWorld(world, entities.PlayerId),
                            out GameVerticalMotionSnapshot state),
                        "The prepared-boundary vertical state was unavailable.");
                    return state;
                },
                out GameVerticalMotionSnapshot snapshot),
            "The prepared-boundary vertical-state read did not run.");
        return snapshot;
    }

    static WorldPosition Position(long x, int y, long z)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
            throw new InvalidOperationException("A prepared-boundary fixture point was invalid.");
        return value;
    }
}

static TestMetrics AuthoritativeVerticalArithmeticOverflowRecovers()
{
    WorldPack pack = CreatePhysicsTerrainShellWorldPack();
    var extreme = new GameVerticalMotionOptions(
        JumpSpeedMillimetersPerSecond: 11_000,
        GravityMillimetersPerSecondSquared: int.MinValue,
        RecoveryMinimumYMillimeters: -50_000,
        RecoveryPosition: GamePlayableWorld.PlayerStart);
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack,
        verticalMotionOptions: extreme);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_108E))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(108_014));
    Expect(entered.IsEntered, "The vertical-overflow actor did not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.PopulateConceptDemo(
                world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
        "The vertical-overflow fixture could not populate.");

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, input.ControlSession);
    WorldPosition start = Position(2_000, 0, 2_000);
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, _, game) => game.WorldActions.RelocateActor(
                world, game.WorldState, ResolveInWorld(world, entities.PlayerId), start,
                out WorldPosition _), out GameWorldActionStatus relocated) &&
        relocated == GameWorldActionStatus.Accepted,
        "The vertical-overflow actor could not enter the flat fixture.");
    Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.Jump, true), out GamePlayerCommand jump) &&
        link.ApplyCommand(jump).Jump == GameWorldActionStatus.Accepted,
        "The vertical-overflow jump was not admitted.");

    for (int tick = 0; tick < 120; tick++)
    {
        host.Tick(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
        if (ReadVertical().CumulativeRecoveries != 0)
            break;
    }
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameVerticalMotionSnapshot>(
            (world, _, game) =>
            {
                Expect(game.WorldActions.TryReadVerticalMotion(
                        world, ResolveInWorld(world, entities.PlayerId),
                        out GameVerticalMotionSnapshot state),
                    "The recovered vertical state was unavailable.");
                return state;
            }, out GameVerticalMotionSnapshot vertical),
        "The recovered vertical state could not be read.");
    WorldPosition final = link.ReadPosition();
    Expect(final.GlobalXMillimeters == GamePlayableWorld.PlayerStart.GlobalXMillimeters &&
        final.GlobalZMillimeters == GamePlayableWorld.PlayerStart.GlobalZMillimeters &&
        vertical.IsGrounded &&
        vertical.CumulativeRecoveries == 1,
        "Unrepresentable vertical integration did not use the bounded recovery path: " +
        $"position={final}, vertical={vertical}.");

    return new TestMetrics(
        HashText($"{final}|{vertical.CumulativeRecoveries}"),
        0, 0, 0, 0, 0, pack.Length, 0, 0, 0, 0, 0,
        "Unrepresentable fixed-point velocity/displacement is admitted with Int128 arithmetic and " +
        "uses authoritative safe recovery instead of escaping as OverflowException.",
        WorldPackBytes: pack.Length);

    GameVerticalMotionSnapshot ReadVertical()
    {
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameVerticalMotionSnapshot>(
                (world, _, game) =>
                {
                    Expect(game.WorldActions.TryReadVerticalMotion(
                            world, ResolveInWorld(world, entities.PlayerId),
                            out GameVerticalMotionSnapshot state),
                        "The recovered vertical state was unavailable.");
                    return state;
                }, out GameVerticalMotionSnapshot state),
            "The recovered vertical state could not be read.");
        return state;
    }

    static WorldPosition Position(long x, int y, long z)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
            throw new InvalidOperationException("A vertical-overflow fixture point was invalid.");
        return value;
    }
}

static TestMetrics VerticalMomentumIsGenerationLocalAcrossRestore()
{
    string root = TempRoot("p0-1d-vertical-restore");
    try
    {
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_1089));
        WorldPack pack = CreatePhysicsTerrainShellWorldPack();
        var composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32), baseWorldPack: pack);
        using var host = new InternalHost(composition, RuntimeOptions(identity));
        host.Start();
        ServerRuntime runtime = host.RuntimeForTesting;
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(new ParticipantId(1089));
        Expect(entered.IsEntered, "The vertical restore actor did not enter.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
                (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
                out GamePlayableWorldEntities entities),
            "The vertical restore fixture could not populate.");
        var input = new ClientWorldInputSequencer();
        var link = new PlayableOfflineDirectLink(runtime.Game, entities.PlayerId, input.ControlSession);
        WorldPosition start = Position(
            GamePlayableWorld.PlayerStart.GlobalXMillimeters,
            0,
            GamePlayableWorld.PlayerStart.GlobalZMillimeters);
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, generationId, game) => game.WorldActions.RelocateActor(
                    world, game.WorldState, ResolveInWorld(world, entities.PlayerId),
                    start, out WorldPosition _), out GameWorldActionStatus relocated) &&
            relocated == GameWorldActionStatus.Accepted,
            "The vertical restore actor could not reach BaseTerrain.");
        Expect(input.TryCreateCommand(new ClientWorldInputSample(
                0, 0, GamePlayerActions.Jump, true), out GamePlayerCommand jump) &&
            link.ApplyCommand(jump).Jump == GameWorldActionStatus.Accepted,
            "The pre-save jump was not admitted.");
        for (int tick = 0; tick < 12; tick++) host.Tick();
        WorldPosition savedPosition = link.ReadPosition();
        GameVerticalMotionSnapshot beforeSave = ReadVertical();
        Expect(beforeSave.IsAirborne && beforeSave.VelocityMillimetersPerSecond > 0,
            "The save point did not contain upward generation-local momentum.");

        var persistence = new GameRuntimePersistence(
            runtime.Game,
            composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        GameSaveResult saved = persistence.TrySave(
            new SaveSetContext(1, 1_700_001_080_009), static capture => capture());
        Expect(saved.Succeeded, "The airborne save failed.");
        for (int tick = 0; tick < 10; tick++) host.Tick();
        Expect(link.ReadPosition() != savedPosition, "The airborne authority did not diverge after save.");

        GameLoadResult loaded = persistence.TryLoad();
        Expect(loaded.Succeeded, "The airborne save did not restore.");
        WorldPosition restored = ReadWorldPosition(runtime.Game, entities.PlayerId);
        GameVerticalMotionSnapshot afterLoad = ReadVertical();
        Expect(restored == savedPosition && afterLoad.IsAirborne &&
            afterLoad.VelocityMillimetersPerSecond == 0,
            "Restore persisted provider momentum or failed to reconstruct unsupported position as airborne.");
        host.Tick();
        WorldPosition afterGravity = ReadWorldPosition(runtime.Game, entities.PlayerId);
        Expect(afterGravity.YMillimeters < restored.YMillimeters,
            "A restored unsupported actor did not begin falling from zero vertical velocity.");

        return new TestMetrics(
            HashText($"{savedPosition}|{restored}|{afterGravity}"),
            0, 0, 0, 0, 0,
            saved.Manifest?.Artifacts.Sum(static artifact => artifact.ByteCount) ?? 0,
            0, 0, 0, 0, 0,
            "WorldPosition persists; vertical momentum does not. Generation initialization reconstructs unsupported saved position as Airborne at zero velocity and gravity resumes.");

        GameVerticalMotionSnapshot ReadVertical()
        {
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameVerticalMotionSnapshot>(
                    (world, _, game) =>
                    {
                        Expect(game.WorldActions.TryReadVerticalMotion(
                                world, ResolveInWorld(world, entities.PlayerId),
                                out GameVerticalMotionSnapshot state),
                            "The restored vertical state was unavailable.");
                        return state;
                    }, out GameVerticalMotionSnapshot snapshot),
                "The restored vertical-state read did not run.");
            return snapshot;
        }

        static WorldPosition Position(long x, int y, long z)
        {
            if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
                throw new InvalidOperationException("A vertical restore point was invalid.");
            return value;
        }
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static TestMetrics VerticalStateFencesReusedEntitySlots()
{
    WorldPack pack = CreatePhysicsTerrainShellWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32), baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1090))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(1090));
    Expect(entered.IsEntered, "The reused-slot vertical actor did not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
        "The reused-slot fixture could not populate.");

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(runtime.Game, entities.PlayerId, input.ControlSession);
    WorldPosition supported = Position(
        GamePlayableWorld.PlayerStart.GlobalXMillimeters,
        0,
        GamePlayableWorld.PlayerStart.GlobalZMillimeters);
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, _, game) => game.WorldActions.RelocateActor(
                world, game.WorldState, ResolveInWorld(world, entities.PlayerId),
                supported, out WorldPosition _), out GameWorldActionStatus relocated) &&
        relocated == GameWorldActionStatus.Accepted,
        "The reused-slot actor could not reach supported terrain.");
    Expect(input.TryCreateCommand(new ClientWorldInputSample(
            0, 0, GamePlayerActions.Jump, true), out GamePlayerCommand jump) &&
        link.ApplyCommand(jump).Jump == GameWorldActionStatus.Accepted,
        "The retired slot was not airborne before reuse.");

    WorldPosition unsupported = Position(12_500, 3_000, 12_500);
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, Entity>(
            (world, _, game) =>
            {
                Entity retired = ResolveInWorld(world, entities.PlayerId);
                Expect(world.TryDestroy(retired), "The airborne actor could not be retired.");
                Expect(world.TryInstantiate(game.Content.Character, out Entity replacement),
                    "The replacement actor could not be created.");
                Expect(replacement.Index == retired.Index && replacement.Generation != retired.Generation,
                    "The ECS fixture did not reuse the intended slot with new generation lineage.");
                Expect(game.WorldState.TrySetPosition(world, replacement, unsupported),
                    "The replacement actor could not enter unsupported resident space.");
                Expect(game.WorldActions.BeginControlSession(
                        world, replacement, GameControlSessionEpoch.Create()) ==
                    GameWorldActionStatus.Accepted,
                    "The replacement actor control lineage was not admitted.");
                Expect(game.WorldActions.TryReadVerticalMotion(
                        world, replacement, out GameVerticalMotionSnapshot vertical) &&
                    vertical.IsAirborne,
                    "The replacement actor was not reconstructed as airborne.");
                return replacement;
            }, out Entity replacementActor),
        "The reused-slot mutation did not run.");

    host.Tick();
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, WorldPosition>(
            (world, _, game) =>
            {
                Expect(game.WorldActions.TryReadVerticalMotion(
                        world, replacementActor, out GameVerticalMotionSnapshot vertical) &&
                    vertical.IsAirborne,
                    "The replacement generation lost its vertical state on the next tick.");
                Expect(game.WorldState.TryReadPosition(world, replacementActor, out WorldPosition position),
                    "The replacement generation lost its position on the next tick.");
                return position;
            }, out WorldPosition afterTick) && afterTick.YMillimeters < unsupported.YMillimeters,
        "A replacement generation inherited the retired actor's stale active-list identity.");

    return new TestMetrics(
        HashText($"{replacementActor}|{unsupported}|{afterTick}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "An unsupported actor reusing an airborne ECS slot owns a fresh active entry and continues falling under its new generation.");

    static WorldPosition Position(long x, int y, long z)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition value))
            throw new InvalidOperationException("A reused-slot fixture point was invalid.");
        return value;
    }
}

static TestMetrics SweptMotionPreservesGamePolicy()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(
            new PersistenceIdentityOptions(new PersistentWorldId(0x80_1082)),
            frequencyHz: 1));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0x1082));
    Expect(entered.IsEntered, "The swept-policy actor could not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
        "The swept-policy fixture could not populate.");
    host.Tick();

    Expect(WorldPosition.TryFromGlobalMillimeters(
            13_500, 0, 7_000, out WorldPosition startRequest),
        "The swept-policy start was not representable.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, _, game) => game.WorldActions.RelocateActor(
                world,
                game.WorldState,
                ResolveInWorld(world, entities.PlayerId),
                startRequest,
                out WorldPosition _),
            out GameWorldActionStatus relocation) &&
        relocation == GameWorldActionStatus.Accepted,
        "The swept-policy actor could not reach its clear start.");

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, input.ControlSession);
    WorldPosition start = link.ReadPosition();
    Send(GameLocomotionUpdate.CreateDirect(0, GamePlayerCommand.MaximumAxisMagnitude));
    host.Tick();
    WorldPosition directBlocked = link.ReadPosition();
    Expect(directBlocked == start && ReadActiveLocomotionCount(runtime.Game) == 1,
        $"A one-second Direct tunneled through the thin wall or lost retention: {directBlocked}.");

    Send(GameLocomotionUpdate.CreateDestination(13_500, 13_000));
    host.Tick();
    WorldPosition destinationBlocked = link.ReadPosition();
    Expect(destinationBlocked == start && ReadActiveLocomotionCount(runtime.Game) == 0,
        $"A one-step Destination tunneled through the thin wall or remained retained: {destinationBlocked}.");

    return new TestMetrics(
        HashText($"{start}:{directBlocked}:{destinationBlocked}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "At a deliberately large one-second step, swept blocking preserves Game policy: Direct remains retained while the impossible straight Destination retires.",
        InputCommands: input.Snapshot().CommandsProduced);

    void Send(in GameLocomotionUpdate locomotion)
    {
        Expect(input.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    locomotion,
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand command),
            $"The swept-policy {locomotion.Kind} command could not be formed.");
        GamePlayerCommandResult result = link.ApplyCommand(command);
        Expect(result.Locomotion == GameWorldActionStatus.Accepted,
            $"The swept-policy {locomotion.Kind} command was not accepted: {result.Locomotion}.");
    }
}

static TestMetrics PlayableMovementCadenceMatrix()
{
    int[] frequencies = [20, 60, 120];
    var positions = new List<string>(frequencies.Length);
    ulong commands = 0;
    foreach (int frequency in frequencies)
    {
        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        var composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32),
            baseWorldPack: pack);
        using var host = new InternalHost(
            composition,
            RuntimeOptions(
                new PersistenceIdentityOptions(new PersistentWorldId(checked((ulong)0x80_1000 + (ulong)frequency))),
                frequencyHz: frequency));
        host.Start();
        ServerRuntime runtime = host.RuntimeForTesting;
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(new ParticipantId(checked((ulong)frequency)));
        Expect(entered.IsEntered, $"The {frequency} Hz cadence actor could not enter.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
            $"The {frequency} Hz cadence fixture could not populate.");
        host.Tick();

        var input = new ClientWorldInputSequencer();
        var link = new PlayableOfflineDirectLink(
            runtime.Game, entities.PlayerId, input.ControlSession);
        Expect(input.TryCreateCommand(
            new ClientWorldInputSample(1, 0, GamePlayerActions.None, true),
            out GamePlayerCommand command) &&
            link.ApplyMovement(command) == GameWorldActionStatus.Accepted,
            $"The {frequency} Hz cadence rejected retained Direct intent.");
        for (int step = 0; step < frequency; step++)
            host.Tick();
        WorldPosition position = link.ReadPosition();
        Expect(position.GlobalXMillimeters ==
                GamePlayableWorld.PlayerStart.GlobalXMillimeters +
                GameWorldActions.FullInputSpeedMillimetersPerSecond &&
            position.GlobalZMillimeters == GamePlayableWorld.PlayerStart.GlobalZMillimeters,
            $"One second of full input changed meaning at {frequency} Hz: {position}.");
        Expect(input.TryCreateCommand(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true),
            out GamePlayerCommand stop) && link.ApplyMovement(stop) == GameWorldActionStatus.Accepted,
            $"The {frequency} Hz cadence rejected Direct stop.");
        host.Tick();
        Expect(link.ReadPosition() == position,
            $"The {frequency} Hz actor continued after Direct stop.");
        positions.Add($"{frequency}:{position.GlobalXMillimeters}:{position.YMillimeters}:{position.GlobalZMillimeters}");
        commands += input.Snapshot().CommandsProduced;
    }

    return new TestMetrics(
        HashText(string.Join('|', positions)),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "One retained Direct command over one authoritative second at 20/60/120 Hz; exact final X/Z equality and release-stop.",
        InputCommands: commands);
}

static TestMetrics PlayableMovementDeliveryRateMatrix()
{
    int[] deliveryRates = [20, 60, 120];
    var positions = new List<string>(deliveryRates.Length);
    ulong commands = 0;
    foreach (int deliveryRate in deliveryRates)
    {
        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        var composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32),
            baseWorldPack: pack);
        using var host = new InternalHost(
            composition,
            RuntimeOptions(
                new PersistenceIdentityOptions(new PersistentWorldId(
                    checked((ulong)0x80_1100 + (ulong)deliveryRate))),
                frequencyHz: 60));
        host.Start();
        ServerRuntime runtime = host.RuntimeForTesting;
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(
            new ParticipantId(checked((ulong)0x1100 + (ulong)deliveryRate)));
        Expect(entered.IsEntered, $"The {deliveryRate} Hz delivery actor could not enter.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
            $"The {deliveryRate} Hz delivery fixture could not populate.");
        host.Tick();

        var input = new ClientWorldInputSequencer();
        var link = new PlayableOfflineDirectLink(
            runtime.Game, entities.PlayerId, input.ControlSession);
        for (int step = 0; step < 60; step++)
        {
            if (deliveryRate == 20)
            {
                if (step % 3 == 0)
                    SendDirect(1);
            }
            else if (deliveryRate == 60)
            {
                SendDirect(1);
            }
            else
            {
                // Prove latest-before-step semantics explicitly on the first step:
                // west is fully superseded by east, and authority integrates only east once.
                SendDirect(step == 0 ? -1 : 1);
                SendDirect(1);
            }
            host.Tick();
        }

        WorldPosition position = link.ReadPosition();
        Expect(position.GlobalXMillimeters ==
                GamePlayableWorld.PlayerStart.GlobalXMillimeters +
                GameWorldActions.FullInputSpeedMillimetersPerSecond &&
            position.GlobalZMillimeters == GamePlayableWorld.PlayerStart.GlobalZMillimeters,
            $"Input delivery rate changed one second of movement at {deliveryRate} Hz: {position}.");
        ulong produced = input.Snapshot().CommandsProduced;
        Expect(produced == (ulong)deliveryRate,
            $"The {deliveryRate} Hz delivery case produced {produced} commands.");
        commands += produced;
        positions.Add(
            $"{deliveryRate}:{position.GlobalXMillimeters}:{position.YMillimeters}:{position.GlobalZMillimeters}:{produced}");

        void SendDirect(float x)
        {
            Expect(input.TryCreateCommand(
                    new ClientWorldInputSample(x, 0, GamePlayerActions.None, true),
                    out GamePlayerCommand command) &&
                link.ApplyMovement(command) == GameWorldActionStatus.Accepted,
                $"The {deliveryRate} Hz delivery case rejected Direct state.");
        }
    }

    return new TestMetrics(
        HashText(string.Join('|', positions)),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "Fixed 60 Hz authority with identical Direct delivered at 20/60/120 Hz; west then east before one step proves latest-state integration exactly once.",
        InputCommands: commands);
}

static TestMetrics PlayableDestinationCadenceMatrix()
{
    int[] frequencies = [20, 60, 120];
    var arrivals = new List<string>(frequencies.Length);
    ulong commands = 0;
    foreach (int frequency in frequencies)
    {
        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        var composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32),
            baseWorldPack: pack);
        using var host = new InternalHost(
            composition,
            RuntimeOptions(
                new PersistenceIdentityOptions(new PersistentWorldId(
                    checked((ulong)0x80_1200 + (ulong)frequency))),
                frequencyHz: frequency));
        host.Start();
        ServerRuntime runtime = host.RuntimeForTesting;
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(
            new ParticipantId(checked((ulong)0x1200 + (ulong)frequency)));
        Expect(entered.IsEntered, $"The {frequency} Hz Destination actor could not enter.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
            $"The {frequency} Hz Destination fixture could not populate.");
        host.Tick();

        long targetX = GamePlayableWorld.PlayerStart.GlobalXMillimeters + 1_234;
        long targetZ = GamePlayableWorld.PlayerStart.GlobalZMillimeters + 777;
        var input = new ClientWorldInputSequencer();
        var link = new PlayableOfflineDirectLink(
            runtime.Game, entities.PlayerId, input.ControlSession);
        Expect(input.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDestination(targetX, targetZ),
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand destination),
            $"The {frequency} Hz Destination command could not be formed.");
        GamePlayerCommandResult admitted = link.ApplyCommand(destination);
        Expect(admitted.WasAdmitted && admitted.Locomotion == GameWorldActionStatus.Accepted,
            $"The {frequency} Hz Destination was not admitted.");

        for (int step = 0; step < frequency; step++)
            host.Tick();
        WorldPosition arrived = link.ReadPosition();
        Expect(arrived.GlobalXMillimeters == targetX && arrived.GlobalZMillimeters == targetZ,
            $"The {frequency} Hz Destination did not clamp to the exact target: {arrived}.");
        Expect(ReadActiveLocomotionCount(runtime.Game) == 0,
            $"The {frequency} Hz completed Destination remained active.");
        host.Tick();
        Expect(link.ReadPosition() == arrived,
            $"The {frequency} Hz completed Destination drifted after arrival.");
        commands += input.Snapshot().CommandsProduced;
        arrivals.Add($"{frequency}:{arrived}:{input.Snapshot().CommandsProduced}");
    }

    return new TestMetrics(
        HashText(string.Join('|', arrivals)),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "One latched diagonal Destination command at 20/60/120 Hz; exact final clamp, automatic retirement, and no post-arrival drift.",
        InputCommands: commands);
}

static TestMetrics PlayableDestinationProgressCadenceMatrix()
{
    const long targetX = 5_123;
    const long targetZ = 4_777;
    int[] frequencies = [20, 60, 120];
    var observations = new List<string>(frequencies.Length);
    WorldPosition? oneSecondReference = null;
    ulong commands = 0;
    foreach (int frequency in frequencies)
    {
        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        var composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32),
            baseWorldPack: pack);
        using var host = new InternalHost(
            composition,
            RuntimeOptions(
                new PersistenceIdentityOptions(new PersistentWorldId(
                    checked((ulong)0x80_1250 + (ulong)frequency))),
                frequencyHz: frequency));
        host.Start();
        ServerRuntime runtime = host.RuntimeForTesting;
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(
            new ParticipantId(checked((ulong)0x1250 + (ulong)frequency)));
        Expect(entered.IsEntered,
            $"The {frequency} Hz long-Destination actor could not enter.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
            $"The {frequency} Hz long-Destination fixture could not populate.");
        host.Tick();

        var input = new ClientWorldInputSequencer();
        var link = new PlayableOfflineDirectLink(
            runtime.Game, entities.PlayerId, input.ControlSession);
        Expect(input.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDestination(targetX, targetZ),
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand destination),
            $"The {frequency} Hz long Destination command could not be formed.");
        GamePlayerCommandResult admitted = link.ApplyCommand(destination);
        Expect(admitted.WasAdmitted && admitted.Locomotion == GameWorldActionStatus.Accepted,
            $"The {frequency} Hz long Destination was not admitted.");

        for (int step = 0; step < frequency; step++)
            host.Tick();
        WorldPosition afterOneSecond = link.ReadPosition();
        oneSecondReference ??= afterOneSecond;
        Expect(afterOneSecond == oneSecondReference.Value,
            $"The {frequency} Hz long Destination diverged after one second: {afterOneSecond}.");
        Expect(ReadActiveLocomotionCount(runtime.Game) == 1,
            $"The {frequency} Hz long Destination arrived before the in-flight observation.");

        int remainingSteps = frequency * 6;
        while (ReadActiveLocomotionCount(runtime.Game) != 0 && remainingSteps-- > 0)
            host.Tick();
        WorldPosition arrived = link.ReadPosition();
        Expect(remainingSteps >= 0 &&
            arrived.GlobalXMillimeters == targetX && arrived.GlobalZMillimeters == targetZ,
            $"The {frequency} Hz long Destination did not reach its exact endpoint: {arrived}.");
        host.Tick();
        Expect(link.ReadPosition() == arrived,
            $"The {frequency} Hz long Destination drifted after arrival.");

        commands += input.Snapshot().CommandsProduced;
        observations.Add($"{frequency}:{afterOneSecond}:{arrived}");
    }

    return new TestMetrics(
        HashText(string.Join('|', observations)),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "One long non-cardinal Destination at 20/60/120 Hz; exact equal in-flight position after one authoritative second, then exact arrival and no drift.",
        InputCommands: commands);
}

static TestMetrics LocomotionUpdateSemantics()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1300))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0x1300));
    Expect(entered.IsEntered, "The locomotion-update actor could not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
        (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
        out GamePlayableWorldEntities entities),
        "The locomotion-update fixture could not populate.");
    host.Tick();

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, input.ControlSession);
    WorldPosition start = GamePlayableWorld.PlayerStart;

    GamePlayerCommandResult destination = Send(
        GameLocomotionUpdate.CreateDestination(
            start.GlobalXMillimeters + 2_000,
            start.GlobalZMillimeters));
    Expect(destination.Locomotion == GameWorldActionStatus.Accepted,
        "The action-composition Destination was rejected.");
    host.Tick();
    WorldPosition beforeAction = link.ReadPosition();
    GamePlayerCommandResult actionOnly = Send(
        GameLocomotionUpdate.Unchanged,
        GamePlayerActions.Interact,
        entities.CabinDoorId);
    Expect(actionOnly.WasAdmitted &&
        actionOnly.Locomotion == GameWorldActionStatus.NotRequested &&
        actionOnly.Interaction == GameWorldActionStatus.Accepted,
        "An action-only Unchanged command did not compose independently.");
    host.Tick();
    Expect(link.ReadPosition().GlobalXMillimeters == beforeAction.GlobalXMillimeters + 100 &&
        ReadActiveLocomotionCount(runtime.Game) == 1,
        "An action-only command cancelled or duplicated the active Destination.");

    Reset();
    _ = Send(GameLocomotionUpdate.CreateDestination(
        start.GlobalXMillimeters - 1_000, start.GlobalZMillimeters));
    _ = Send(GameLocomotionUpdate.CreateDestination(
        start.GlobalXMillimeters + 1_000, start.GlobalZMillimeters));
    host.Tick();
    Expect(link.ReadPosition().GlobalXMillimeters == start.GlobalXMillimeters + 100,
        "Destination west then east did not integrate the latest destination once.");

    Reset();
    _ = Send(GameLocomotionUpdate.CreateDestination(
        start.GlobalXMillimeters + 1_000, start.GlobalZMillimeters));
    _ = Send(GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MinimumAxisMagnitude, 0));
    host.Tick();
    Expect(link.ReadPosition().GlobalXMillimeters == start.GlobalXMillimeters - 100,
        "Direct did not supersede the retained Destination.");

    Reset();
    _ = Send(GameLocomotionUpdate.CreateDestination(
        start.GlobalXMillimeters + 1_000, start.GlobalZMillimeters));
    _ = Send(GameLocomotionUpdate.Stop);
    host.Tick();
    Expect(link.ReadPosition() == start && ReadActiveLocomotionCount(runtime.Game) == 0,
        "Explicit Stop did not cancel Destination.");

    Reset();
    _ = Send(GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0));
    GamePlayerCommandResult invalidDestination = Send(
        GameLocomotionUpdate.CreateDestination(long.MaxValue, long.MaxValue));
    Expect(invalidDestination.WasAdmitted &&
        invalidDestination.Locomotion == GameWorldActionStatus.OutsidePreparedWorld,
        "The out-of-world Destination did not reject as an admitted effect.");
    host.Tick();
    Expect(link.ReadPosition().GlobalXMillimeters == start.GlobalXMillimeters + 100,
        "A rejected Destination replaced the previous valid Direct state.");

    Reset();
    _ = Send(GameLocomotionUpdate.CreateDestination(
        start.GlobalXMillimeters + 1_000, start.GlobalZMillimeters));
    var replacement = new ClientWorldInputSequencer();
    var replacementLink = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, replacement.ControlSession);
    host.Tick();
    Expect(replacementLink.ReadPosition() == start && ReadActiveLocomotionCount(runtime.Game) == 0,
        "Control-session replacement did not cancel Destination.");
    Expect(replacement.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDestination(
                    start.GlobalXMillimeters + 1_000, start.GlobalZMillimeters),
                GamePlayerActions.None,
                true),
            out GamePlayerCommand replacementDestination) &&
        replacementLink.ApplyCommand(replacementDestination).Locomotion ==
            GameWorldActionStatus.Accepted,
        "The replacement session could not admit Destination.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
        (world, _, game) => game.WorldActions.EndControlSession(
            world, ResolveInWorld(world, entities.PlayerId), replacement.ControlSession),
        out GameWorldActionStatus ended) && ended == GameWorldActionStatus.Accepted,
        "The Destination control session could not end.");
    host.Tick();
    Expect(replacementLink.ReadPosition() == start && ReadActiveLocomotionCount(runtime.Game) == 0,
        "Ending the control session did not cancel Destination.");

    return new TestMetrics(
        HashText($"{beforeAction}|{link.ReadPosition()}|{input.Snapshot().CommandsProduced}|{replacement.Snapshot().CommandsProduced}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "Unchanged action composition; Destination replacement/latest-state; Direct/Stop/session cancellation; invalid target preserves prior state.",
        InputCommands: input.Snapshot().CommandsProduced + replacement.Snapshot().CommandsProduced);

    GamePlayerCommandResult Send(
        in GameLocomotionUpdate locomotion,
        GamePlayerActions held = GamePlayerActions.None,
        PersistentEntityId target = default)
    {
        Expect(input.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(locomotion, held, true),
                out GamePlayerCommand command),
            $"The {locomotion.Kind} semantic command could not be formed.");
        return link.ApplyCommand(command, target);
    }

    void Reset()
    {
        GamePlayerCommandResult stopped = Send(GameLocomotionUpdate.Stop);
        Expect(stopped.WasAdmitted && stopped.Locomotion == GameWorldActionStatus.Accepted,
            "The locomotion fixture could not stop before reset.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) => game.WorldState.TrySetPosition(
                world, ResolveInWorld(world, entities.PlayerId), start),
            out bool reset) && reset,
            "The locomotion fixture could not reset actor position.");
        host.Tick();
        Expect(link.ReadPosition() == start && ReadActiveLocomotionCount(runtime.Game) == 0,
            "The locomotion fixture reset was not canonical.");
    }
}

static TestMetrics AuthoritativeRelocationCancelsLocomotion()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1350))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0x1350));
    Expect(entered.IsEntered, "The relocation actor could not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
        (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
        out GamePlayableWorldEntities entities),
        "The relocation fixture could not populate.");
    host.Tick();

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, input.ControlSession);

    Send(GameLocomotionUpdate.CreateDestination(30_000, 25_000));
    host.Tick();
    Expect(ReadActiveLocomotionCount(runtime.Game) == 1,
        "Destination was not active before relocation.");
    WorldPosition firstRelocation = Relocate(GamePlayableWorld.AnimalPosition);
    host.Tick();
    Expect(link.ReadPosition() == firstRelocation && ReadActiveLocomotionCount(runtime.Game) == 0,
        "Destination resumed or rewound the actor after authoritative relocation.");

    Send(GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0));
    host.Tick();
    Expect(ReadActiveLocomotionCount(runtime.Game) == 1,
        "Direct was not active before relocation.");
    WorldPosition secondRelocation = Relocate(GamePlayableWorld.PlayerStart);
    host.Tick();
    Expect(link.ReadPosition() == secondRelocation && ReadActiveLocomotionCount(runtime.Game) == 0,
        "Direct continued after authoritative relocation.");

    return new TestMetrics(
        HashText($"{firstRelocation}|{secondRelocation}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "Successful game-owned relocation resolves terrain and atomically cancels retained Direct/Destination; the primitive position writer remains policy-free.",
        InputCommands: input.Snapshot().CommandsProduced);

    void Send(in GameLocomotionUpdate locomotion)
    {
        Expect(input.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(locomotion, GamePlayerActions.None, true),
                out GamePlayerCommand command),
            $"The pre-relocation {locomotion.Kind} command could not be formed.");
        GamePlayerCommandResult result = link.ApplyCommand(command);
        Expect(result.WasAdmitted && result.Locomotion == GameWorldActionStatus.Accepted,
            $"The pre-relocation {locomotion.Kind} command was not admitted.");
    }

    WorldPosition Relocate(in WorldPosition requested)
    {
        WorldPosition target = requested;
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, WorldPosition>(
            (world, _, game) =>
            {
                Entity actor = ResolveInWorld(world, entities.PlayerId);
                GameWorldActionStatus status = game.WorldActions.RelocateActor(
                    world, game.WorldState, actor, target, out WorldPosition relocated);
                Expect(status == GameWorldActionStatus.Accepted,
                    $"Authoritative relocation failed: {status}.");
                return relocated;
            }, out WorldPosition relocated),
            "Authoritative relocation had no active generation.");
        Expect(ReadActiveLocomotionCount(runtime.Game) == 0,
            "Successful relocation did not immediately retire retained locomotion.");
        return relocated;
    }
}

static TestMetrics BlockedRelocationIsTransactional()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1375))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(0x1375));
    Expect(entered.IsEntered, "The occupied-relocation actor could not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
        "The occupied-relocation fixture could not populate.");
    host.Tick();

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, input.ControlSession);
    Expect(input.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDirect(
                    0, GamePlayerCommand.MaximumAxisMagnitude),
                GamePlayerActions.None,
                true),
            out GamePlayerCommand direct) &&
        link.ApplyCommand(direct).Locomotion == GameWorldActionStatus.Accepted &&
        ReadActiveLocomotionCount(runtime.Game) == 1,
        "Direct was not retained before rejected relocation.");

    WorldPosition original = link.ReadPosition();
    Expect(Relocate(GamePlayableWorld.ChestPosition) == GameWorldActionStatus.Blocked,
        "The occupied chest endpoint did not reject relocation.");
    Expect(WorldPosition.TryFromGlobalMillimeters(
            16_000, 1_000, 15_000, out WorldPosition wallTarget) &&
        Relocate(wallTarget) == GameWorldActionStatus.Blocked,
        "The prepared cabin wall endpoint did not reject relocation.");
    Expect(Relocate(GamePlayableWorld.DoorPosition) == GameWorldActionStatus.Blocked,
        "The closed cabin door endpoint did not reject relocation.");
    Expect(WorldPosition.TryFromGlobalMillimeters(
            1_000_000, 0, 1_000_000, out WorldPosition unprepared) &&
        Relocate(unprepared) == GameWorldActionStatus.OutsidePreparedWorld,
        "The unprepared relocation target did not preserve its distinct failure.");
    Expect(link.ReadPosition() == original && ReadActiveLocomotionCount(runtime.Game) == 1,
        "Rejected relocation changed position or retired retained locomotion.");

    Expect(input.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Unchanged,
                GamePlayerActions.Interact,
                true),
            out GamePlayerCommand openDoor) &&
        link.ApplyCommand(openDoor, entities.CabinDoorId).Interaction ==
            GameWorldActionStatus.Accepted,
        "The nearby cabin door could not open before clear relocation.");
    Expect(Relocate(GamePlayableWorld.DoorPosition) == GameWorldActionStatus.Accepted &&
        link.ReadPosition() == GamePlayableWorld.DoorPosition &&
        ReadActiveLocomotionCount(runtime.Game) == 0,
        "A clear relocation did not commit terrain truth and retire retained locomotion.");

    return new TestMetrics(
        HashText($"{original}:{link.ReadPosition()}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "P0.1a rejects static/dynamic/unprepared relocation transactionally, preserves retained locomotion on failure, and commits/cancels only a clear endpoint.",
        InputCommands: input.Snapshot().CommandsProduced);

    GameWorldActionStatus Relocate(in WorldPosition requested)
    {
        WorldPosition target = requested;
        Expect(runtime.Game.TryUseActiveGeneration<
                IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, _, game) =>
                {
                    GameWorldActionStatus status = game.WorldActions.RelocateActor(
                        world,
                        game.WorldState,
                        ResolveInWorld(world, entities.PlayerId),
                        target,
                        out WorldPosition relocated);
                    if (status != GameWorldActionStatus.Accepted)
                        Expect(relocated == default,
                            "Rejected relocation exposed a partially resolved position.");
                    return status;
                },
                out GameWorldActionStatus status),
            "Relocation had no active generation.");
        return status;
    }
}

static TestMetrics IndependentMoveLookKeepsWorldMovementAndFacingSeparate()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var runtime = new ServerRuntime(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1800))));
    runtime.Initialize();
    runtime.Start();
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(118));
    Expect(entered.IsEntered, "Independent-move/look participant did not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
        (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
        out GamePlayableWorldEntities entities),
        "Independent-move/look fixture did not populate.");
    runtime.Tick();

    var input = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(
        runtime.Game, entities.PlayerId, input.ControlSession);
    var profile = new IndependentMoveLookProfile();
    ClientControlWorldPoint east = new(
        GamePlayableWorld.PlayerStart.GlobalXMillimeters + 10_000,
        GamePlayableWorld.PlayerStart.GlobalZMillimeters);
    ClientControlProfileDecision decision = profile.Evaluate(
        0,
        -1,
        GamePlayerActions.None,
        true,
        true,
        east);
    Expect(decision.Command.Locomotion.Kind == GameLocomotionUpdateKind.Direct &&
        decision.Command.Locomotion.DirectX == 0 &&
        decision.Command.Locomotion.DirectZ == GamePlayerCommand.MinimumAxisMagnitude &&
        decision.LookRequested,
        "Independent profile did not preserve fixed north movement and separate east look.");
    Expect(input.TryCreateSemanticCommand(decision.Command, out GamePlayerCommand command) &&
        link.ApplyCommand(command).Locomotion == GameWorldActionStatus.Accepted,
        "Independent world-axis movement command was not admitted.");

    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
        (world, _, game) =>
        {
            GameWorldActionStatus status = game.WorldActions.FaceActorToward(
                world,
                game.WorldState,
                ResolveInWorld(world, entities.PlayerId),
                decision.LookPoint.XMillimeters,
                decision.LookPoint.ZMillimeters,
                out WorldYaw ignored);
            return status;
        },
        out GameWorldActionStatus facing) && facing == GameWorldActionStatus.Accepted,
        "Independent mouse-facing update was not accepted.");
    runtime.Tick();

    WorldPosition moved = link.ReadPosition();
    WorldPublicDescriptor descriptor = runtime.Game.TryUseActiveGeneration<
        IGameGenerationStateProvider, WorldPublicDescriptor>(
        (world, _, game) => game.WorldState.TryReadPublic(
            world,
            ResolveInWorld(world, entities.PlayerId),
            out WorldPublicDescriptor current)
                ? current
                : throw new InvalidOperationException("Player public descriptor was missing."),
        out WorldPublicDescriptor read)
        ? read
        : throw new InvalidOperationException("Independent-facing generation was unavailable.");
    Expect(moved.GlobalXMillimeters == GamePlayableWorld.PlayerStart.GlobalXMillimeters &&
        moved.GlobalZMillimeters == GamePlayableWorld.PlayerStart.GlobalZMillimeters - 100,
        "Facing east rotated fixed-world north movement.");
    Expect(descriptor.Orientation == WorldYaw.QuarterTurn,
        $"East-facing yaw was not canonical: {descriptor.Orientation}.");

    return new TestMetrics(
        HashText($"{moved}|{descriptor.Orientation}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "F3-style fixed-world WASD remains independent from authoritative mouse-facing yaw.",
        InputCommands: input.Snapshot().CommandsProduced);
}

static TestMetrics ContradictoryRestoredDoorFailsClosed()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var runtime = new ServerRuntime(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_2000))));
    runtime.Initialize();
    runtime.Start();

    GameStaging staging = composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x80_2000)));
    ComponentLookup<GameCabinDoorState> doorLookup = staging.World.PrepareLookup<GameCabinDoorState>();
    staging.World.Seal();
    staging.World.Initialize();
    staging.World.BeginRun();
    Expect(staging.World.TryInstantiate(staging.Content.CabinDoor, out Entity door) &&
        staging.WorldState.TrySetPosition(staging.World, door, GamePlayableWorld.DoorPosition) &&
        staging.World.TryWrite(door, doorLookup, new GameCabinDoorState { IsOpen = 1 }),
        "The contradictory private door candidate could not be prepared.");
    var candidate = new GameGenerationState(
        new AccountingStore(),
        staging.Inventory,
        staging.Items,
        staging.Content,
        staging.WorldState,
        staging.SimulationLookup,
        staging.Replication,
        staging.WorldActions);

    bool rejected = false;
    try
    {
        _ = runtime.Game.TryPublishPreparedGeneration(staging.World, candidate, out _);
    }
    catch (InvalidOperationException exception)
    {
        rejected = exception.Message.Contains("contradictory persisted/public state", StringComparison.Ordinal);
    }
    Expect(rejected && runtime.State == ServerRuntimeState.Running,
        "A contradictory restored door was published or faulted the active generation.");

    return new TestMetrics(
        HashText("contradictory-door-rejected"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "Private generation admission rejects persisted open state with closed public orientation.");
}

static TestMetrics DynamicCollisionProjectionCapacityFailsClosed()
{
    const ulong persistentWorld = 0x80_2050;
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var runtime = new ServerRuntime(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(persistentWorld))));
    runtime.Initialize();
    runtime.Start();

    GameStaging staging = composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(persistentWorld)));
    staging.World.Seal();
    staging.World.Initialize();
    staging.World.BeginRun();
    for (int index = 0; index <= 16; index++)
    {
        Expect(staging.Inventory.TryCreateContainer(
                staging.World, staging.Content.WorldChest, 1, out Entity chest) &&
            WorldPosition.TryFromGlobalMillimeters(
                4_000 + index * 100L,
                1_000,
                4_000,
                out WorldPosition position) &&
            staging.WorldState.TrySetPosition(staging.World, chest, position),
            $"Dynamic blocker {index} could not be prepared.");
    }

    var candidate = new GameGenerationState(
        new AccountingStore(),
        staging.Inventory,
        staging.Items,
        staging.Content,
        staging.WorldState,
        staging.SimulationLookup,
        staging.Replication,
        staging.WorldActions);
    bool rejected = false;
    try
    {
        _ = runtime.Game.TryPublishPreparedGeneration(staging.World, candidate, out _);
    }
    catch (InvalidOperationException exception)
    {
        rejected = exception.Message.Contains(
            "dynamic-blocker set is full", StringComparison.Ordinal);
    }
    Expect(rejected && runtime.State == ServerRuntimeState.Running,
        "A 17th projected dynamic blocker published partial physical truth or faulted authority.");

    return new TestMetrics(
        HashText("dynamic-collision-capacity-rejected"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "The generation-owned 16-entry effective dynamic collision projection rejects a 17th blocker before publication.");
}

static TestMetrics ControlSessionLineageFencesRetainedDirect()
{
    WorldPack pack = GamePlayableWorld.CreateWorldPack();
    var composition = new UltraSimGameComposition(
        new InventoryLimits(32, 128, 32),
        baseWorldPack: pack);
    using var host = new InternalHost(
        composition,
        RuntimeOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x80_1800))));
    host.Start();
    ServerRuntime runtime = host.RuntimeForTesting;
    using var admission = new AdmissionService(runtime.Game, composition);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(1800));
    Expect(entered.IsEntered, "The control-lineage actor could not enter.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
        (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
        out GamePlayableWorldEntities entities),
        "The control-lineage fixture could not populate.");
    host.Tick();

    var first = new ClientWorldInputSequencer();
    var link = new PlayableOfflineDirectLink(runtime.Game, entities.PlayerId, first.ControlSession);
    Expect(first.TryCreateCommand(
        new ClientWorldInputSample(1, 0, GamePlayerActions.None, true),
        out GamePlayerCommand retained) && link.ApplyMovement(retained) == GameWorldActionStatus.Accepted,
        "The first Direct lineage was not admitted.");
    host.Tick();
    WorldPosition afterFirst = link.ReadPosition();

    var invalid = new GamePlayerCommand(
        first.ControlSession,
        2,
        new GameLocomotionUpdate(
            GameLocomotionUpdateKind.Direct,
            short.MaxValue,
            short.MaxValue,
            0,
            0),
        GamePlayerActions.None,
        GamePlayerActions.None);
    Expect(!invalid.IsValid && link.ApplyMovement(invalid) == GameWorldActionStatus.InvalidCommand,
        "Authority admitted a Direct vector outside the Q15 unit circle.");
    host.Tick();
    WorldPosition afterRejected = link.ReadPosition();
    Expect(afterRejected.GlobalXMillimeters == afterFirst.GlobalXMillimeters + 100,
        "Rejected malformed input replaced the last admitted Direct state.");

    var replacement = new ClientWorldInputSequencer();
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
        (world, _, game) => game.WorldActions.BeginControlSession(
            world, ResolveInWorld(world, entities.PlayerId), replacement.ControlSession),
        out GameWorldActionStatus rebound) && rebound == GameWorldActionStatus.Accepted,
        "The replacement control lineage could not bind.");
    host.Tick();
    WorldPosition afterRebind = link.ReadPosition();
    Expect(afterRebind == afterRejected,
        "Replacing the control lineage did not terminate retained Direct movement.");

    var stale = new GamePlayerCommand(
        first.ControlSession,
        2,
        GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
        GamePlayerActions.None,
        GamePlayerActions.None);
    Expect(link.ApplyMovement(stale) == GameWorldActionStatus.StaleControlSession,
        "A command from the replaced control lineage was admitted.");
    host.Tick();
    Expect(link.ReadPosition() == afterRebind,
        "A stale control lineage restarted locomotion.");

    Expect(replacement.TryCreateCommand(
        new ClientWorldInputSample(1, 0, GamePlayerActions.None, true),
        out GamePlayerCommand current) && link.ApplyMovement(current) == GameWorldActionStatus.Accepted,
        "The replacement lineage could not admit Direct intent.");
    host.Tick();
    WorldPosition afterReplacement = link.ReadPosition();
    Expect(afterReplacement.GlobalXMillimeters == afterRebind.GlobalXMillimeters + 100,
        "The replacement lineage did not move on the authoritative step.");

    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
        (world, _, game) => game.WorldActions.BeginControlSession(
            world, ResolveInWorld(world, entities.PlayerId), replacement.ControlSession),
        out GameWorldActionStatus idempotent) && idempotent == GameWorldActionStatus.Accepted &&
        link.ApplyMovement(current) == GameWorldActionStatus.StaleCommand,
        "Idempotent control binding reset the admitted sequence floor.");
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
        (world, _, game) => game.WorldActions.EndControlSession(
            world, ResolveInWorld(world, entities.PlayerId), replacement.ControlSession),
        out GameWorldActionStatus ended) && ended == GameWorldActionStatus.Accepted,
        "The current control lineage could not end.");
    host.Tick();
    Expect(link.ReadPosition() == afterReplacement,
        "Ending control did not terminate retained Direct movement.");

    return new TestMetrics(
        HashText($"{afterFirst}|{afterRejected}|{afterRebind}|{afterReplacement}"),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        "Malformed Direct rejected without replacement; rebind/end stop retained state; stale epochs reject; idempotent bind preserves sequence floor.",
        InputCommands: first.Snapshot().CommandsProduced + replacement.Snapshot().CommandsProduced);
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

static WorldPack CreatePhysicsTerrainShellWorldPack()
{
    var limits = new WorldPackLimits(
        1 * 1024 * 1024,
        maximumChunks: 1,
        maximumSections: 8,
        maximumFrozenStatics: 0,
        maximumMultiInstances: 0,
        maximumMultiFragments: 0,
        maximumAdditionalTerrainSurfaceFragments: 8,
        maximumAdditionalTerrainSurfaceFragmentsPerChunk: 8);
    var builder = new WorldPackBuilder(
        GameWorldState.DefaultSpace,
        GameContent.WorldFingerprint,
        limits);
    var key = new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0);
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    Array.Fill(materials, checked((ushort)GameTerrainMaterialIds.Grass));
    var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    var basePresence = new ulong[TerrainChunkFormat.SurfacePresenceWordCount];
    TerrainSurfacePresence.FillPresent(basePresence);
    for (int z = 24; z <= 25; z++)
    for (int x = 24; x <= 25; x++)
        TerrainSurfacePresence.SetPresent(
            basePresence, new TerrainCellCoord(checked((byte)x), checked((byte)z)), false);
    for (int z = 22; z <= 25; z++)
    for (int x = 28; x <= 31; x++)
        TerrainSurfacePresence.SetPresent(
            basePresence, new TerrainCellCoord(checked((byte)x), checked((byte)z)), false);
    // A distinct six-meter fall-recovery runway keeps the one-meter grounded-path
    // sampling canary above intact while giving captured takeoff momentum enough
    // unsupported space to cross the recovery envelope.
    for (int z = 28; z <= 31; z++)
    for (int x = 20; x <= 31; x++)
        TerrainSurfacePresence.SetPresent(
            basePresence, new TerrainCellCoord(checked((byte)x), checked((byte)z)), false);
    Expect(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase baseHeight),
        "The P0.1c BaseTerrain elevation was not q5 canonical.");
    Expect(builder.TryAddBaseTerrain(
            key, materials, baseHeight, heights, basePresence, out WorldPackBuildError baseError),
        $"The P0.1c base terrain could not be built: {baseError}.");

    AddBand(new WorldSurfaceId(900), new TerrainSurfacePatchBounds(4, 4, 6, 6),
        top: 10_000, bottom: 8_000);
    AddBand(new WorldSurfaceId(1), new TerrainSurfacePatchBounds(4, 4, 6, 6),
        top: 5_000, bottom: null);
    AddBand(new WorldSurfaceId(333), new TerrainSurfacePatchBounds(4, 4, 6, 6),
        top: -5_000, bottom: null);
    AddBand(new WorldSurfaceId(400), new TerrainSurfacePatchBounds(12, 4, 6, 6),
        top: null, bottom: 1_500);
    AddBand(new WorldSurfaceId(700), new TerrainSurfacePatchBounds(4, 14, 6, 6),
        top: 400, bottom: 0);
    AddBand(new WorldSurfaceId(2), new TerrainSurfacePatchBounds(16, 14, 6, 6),
        top: 1_000, bottom: 0);
    AddBand(new WorldSurfaceId(50), new TerrainSurfacePatchBounds(28, 22, 4, 4),
        top: 3_000, bottom: null);

    Expect(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError),
        $"The P0.1c terrain-shell WorldPack could not build: {buildError}.");
    return artifact!.Open();

    void AddBand(
        WorldSurfaceId id,
        TerrainSurfacePatchBounds bounds,
        int? top,
        int? bottom)
    {
        WorldTerrainSurfaceFaces faces =
            (top.HasValue ? WorldTerrainSurfaceFaces.Top : WorldTerrainSurfaceFaces.None) |
            (bottom.HasValue ? WorldTerrainSurfaceFaces.Bottom : WorldTerrainSurfaceFaces.None);
        var presence = new ulong[bounds.PresenceWordCount];
        Array.Fill(presence, ulong.MaxValue);
        int remainder = bounds.CellCount & 63;
        if (remainder != 0) presence[^1] = (1UL << remainder) - 1;
        var connections = new TerrainSurfaceEdgeConnection[bounds.CellCount];
        ushort[] topMaterials = top.HasValue ? new ushort[bounds.CellCount] : [];
        ushort[] bottomMaterials = bottom.HasValue ? new ushort[bounds.CellCount] : [];
        TerrainHeightOffset[] topHeights = top.HasValue
            ? new TerrainHeightOffset[bounds.VertexCount]
            : [];
        TerrainHeightOffset[] bottomHeights = bottom.HasValue
            ? new TerrainHeightOffset[bounds.VertexCount]
            : [];
        Array.Fill(topMaterials, checked((ushort)GameTerrainMaterialIds.Rock));
        Array.Fill(bottomMaterials, checked((ushort)GameTerrainMaterialIds.Rock));
        TerrainHeightPlaneBase topBase = default;
        TerrainHeightPlaneBase bottomBase = default;
        Expect(!top.HasValue || TerrainHeightPlaneBase.TryCreate(top.Value, out topBase),
            "The P0.1c Top elevation was not q5 canonical.");
        Expect(!bottom.HasValue || TerrainHeightPlaneBase.TryCreate(bottom.Value, out bottomBase),
            "The P0.1c Bottom elevation was not q5 canonical.");
        ushort sideMaterial = top.HasValue && bottom.HasValue
            ? checked((ushort)GameTerrainMaterialIds.Rock)
            : (ushort)0;
        Expect(builder.TryAddAdditionalTerrainSurfaceBand(
                key,
                id,
                bounds,
                faces,
                presence,
                connections,
                topMaterials,
                topBase,
                topHeights,
                bottomMaterials,
                bottomBase,
                bottomHeights,
                sideMaterial,
                out WorldPackBuildError error),
            $"The P0.1c terrain shell {id} could not be built: {error}.");
    }
}

static WorldPack CreateStaticTopPolicyWorldPack()
{
    var limits = new WorldPackLimits(
        1 * 1024 * 1024,
        maximumChunks: 1,
        maximumSections: 8,
        maximumFrozenStatics: 2,
        maximumMultiInstances: 0,
        maximumMultiFragments: 0);
    var builder = new WorldPackBuilder(
        GameWorldState.DefaultSpace,
        GameContent.WorldFingerprint,
        limits);
    var key = new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0);
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    Array.Fill(materials, checked((ushort)GameTerrainMaterialIds.Grass));
    var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    Expect(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase) &&
        builder.TryAddBaseTerrain(key, materials, heightBase, heights, out _),
        "The static-top-policy BaseTerrain could not be built.");

    AddStatic(1, 4_000, 4_000, 100, WorldStaticFlags.Collidable);
    AddStatic(2, 8_000, 8_000, 500,
        WorldStaticFlags.Collidable | WorldStaticFlags.NoTopSupport);
    Expect(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError error),
        $"The static-top-policy WorldPack could not build: {error}.");
    return artifact!.Open();

    void AddStatic(ulong id, long x, long z, int halfExtent, WorldStaticFlags flags)
    {
        Expect(WorldPosition.TryFromGlobalMillimeters(x, 0, z, out WorldPosition position),
            "A static-top-policy placement was invalid.");
        var bounds = new WorldStaticBounds(
            x - halfExtent, 0, z - halfExtent,
            x + halfExtent + 1, 1_000, z + halfExtent + 1);
        Expect(builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                new WorldStaticId(id),
                new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Rock),
                position,
                WorldYaw.Zero,
                0,
                flags,
                bounds), out WorldPackBuildError error),
            $"Static-top-policy placement {id} was rejected: {error}.");
    }
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

static int ReadActiveLocomotionCount(ServerGameFacet runtime)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, int>(
        (_, _, game) => game.WorldActions.ActiveLocomotionCount,
        out int count),
        "Active locomotion count read did not run.");
    return count;
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
    long WorldActionReservedBytes = 0,
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
    private readonly ServerGenerationStateOperation<IGameGenerationStateProvider, GamePlayerCommandResult> _applyCommand;
    private readonly ServerGenerationStateOperation<IGameGenerationStateProvider, GameWorldActionStatus> _begin;
    private readonly ServerGenerationStateOperation<IGameGenerationStateProvider, WorldPosition> _read;
    private readonly GameControlSessionEpoch _controlSession;
    private GamePlayerCommand _command;
    private PersistentEntityId _interactionTarget;

    internal PlayableOfflineDirectLink(
        ServerGameFacet runtime,
        PersistentEntityId playerId,
        GameControlSessionEpoch controlSession)
    {
        _runtime = runtime;
        _playerId = playerId;
        _controlSession = controlSession;
        _apply = ApplyCore;
        _applyCommand = ApplyCommandCore;
        _begin = BeginCore;
        _read = ReadCore;
        if (!_runtime.TryUseActiveGeneration(_begin, out GameWorldActionStatus status) ||
            status != GameWorldActionStatus.Accepted)
        {
            throw new InvalidOperationException($"OfflineDirect control binding failed: {status}.");
        }
    }

    internal GameWorldActionStatus ApplyMovement(in GamePlayerCommand command)
    {
        _command = command;
        if (!_runtime.TryUseActiveGeneration(_apply, out GameWorldActionStatus status))
            throw new InvalidOperationException("OfflineDirect movement had no active generation.");
        return status;
    }

    internal GamePlayerCommandResult ApplyCommand(
        in GamePlayerCommand command,
        PersistentEntityId interactionTarget = default)
    {
        _command = command;
        _interactionTarget = interactionTarget;
        if (!_runtime.TryUseActiveGeneration(_applyCommand, out GamePlayerCommandResult result))
            throw new InvalidOperationException("OfflineDirect command had no active generation.");
        return result;
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
        return game.WorldActions.ApplyPlayerCommand(
            world,
            game.WorldState,
            player,
            _command,
            Entity.Null,
            out GamePlayerCommandResult result) == GameWorldActionStatus.Accepted &&
            result.Locomotion == GameWorldActionStatus.Accepted
            ? GameWorldActionStatus.Accepted
            : result.Admission != GameWorldActionStatus.Accepted
                ? result.Admission
                : result.Locomotion;
    }

    private GamePlayerCommandResult ApplyCommandCore(
        EcsWorld world,
        ulong generationId,
        IGameGenerationStateProvider game)
    {
        if (!world.TryResolvePersistentEntity(_playerId, out Entity player))
            return new GamePlayerCommandResult(
                GameWorldActionStatus.MissingActor,
                GameWorldActionStatus.NotRequested,
                GameWorldActionStatus.NotRequested,
                GameWorldActionStatus.NotRequested,
                GameWorldActionStatus.NotRequested,
                default,
                false);
        Entity target = Entity.Null;
        if (_interactionTarget.IsValid &&
            !world.TryResolvePersistentEntity(_interactionTarget, out target))
        {
            target = Entity.Null;
        }
        _ = game.WorldActions.ApplyPlayerCommand(
            world,
            game.WorldState,
            player,
            _command,
            target,
            out GamePlayerCommandResult result);
        return result;
    }

    private GameWorldActionStatus BeginCore(
        EcsWorld world,
        ulong generationId,
        IGameGenerationStateProvider game)
    {
        if (!world.TryResolvePersistentEntity(_playerId, out Entity player))
            return GameWorldActionStatus.MissingActor;
        return game.WorldActions.BeginControlSession(world, player, _controlSession);
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
