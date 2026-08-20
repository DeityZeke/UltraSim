using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Inventory;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Composition;

/// <summary>Private candidate world and derived content/index state prepared before generation publication.</summary>
public readonly record struct GameStaging(
    EcsWorld World,
    InventoryGenerationState Inventory,
    GameContent Content,
    GameWorldState WorldState,
    ComponentLookup<GameSimulationState> SimulationLookup,
    GameReplicationBindings? Replication = null);

/// <summary>Concrete game composition owning one schema, content set, and generation-state root.</summary>
public sealed class UltraSimGameComposition : IServerComposition, IAdmissionEntityBinder
{
    public const int SpatialLifecycleRecordCapacity = 8_192;
    public const int NetworkLifecycleRecordCapacity = SpatialLifecycleRecordCapacity;
    public const int NetworkCommandCapacity = 256;

    private readonly ConditionalWeakTable<EcsWorld, GameContent> _contentByWorld = new();
    private readonly InventoryLimits _inventoryLimits;
    private readonly int _maximumChunksPerContentArchetype;
    private readonly GameRuntimeProfile _runtimeProfile;
    private readonly WorldPack? _baseWorldPack;

    public UltraSimGameComposition(
        InventoryLimits? inventoryLimits = null,
        int maximumChunksPerContentArchetype = 16,
        GameRuntimeProfile runtimeProfile = GameRuntimeProfile.Local,
        WorldPack? baseWorldPack = null)
    {
        _inventoryLimits = inventoryLimits ?? InventoryLimits.Default;
        _inventoryLimits.Validate();
        if (maximumChunksPerContentArchetype <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunksPerContentArchetype));
        if ((uint)runtimeProfile > (uint)GameRuntimeProfile.Networked)
            throw new ArgumentOutOfRangeException(nameof(runtimeProfile));
        _maximumChunksPerContentArchetype = maximumChunksPerContentArchetype;
        _runtimeProfile = runtimeProfile;
        _baseWorldPack = baseWorldPack;
        Schema = GameSchema.CreateSchema();
        PersistenceCatalog = GameSchema.CreatePersistenceCatalog(static builder =>
            builder.RegisterHistoricalReader<GameSimulationState>(
                savedVersion: 1,
                GameSimulationStateV1Reader.Fingerprint,
                new GameSimulationStateV1Reader()));
    }

    public ComponentSchema Schema { get; }
    public PersistenceCatalog PersistenceCatalog { get; }

    public void Configure(ServerGenerationBuilder generation)
    {
        GameContent content = CompileContent(generation.World);
        (ComponentLookup<GameSimulationState> simulation, GameReplicationBindings? replication) =
            PrepareSimulation(generation.World, _runtimeProfile);
        generation.SetGameState(new GameGenerationState(
            new AccountingStore(),
            new InventoryGenerationState(generation.World, content, _inventoryLimits),
            content,
            CreateWorldState(generation.World, content),
            simulation,
            replication));
    }

    public GameStaging CreateStaging(
        PersistenceRestoreRequirements requirements,
        InventoryLimits? limits = null)
    {
        var world = new EcsWorld(requirements.Schema, CreateWorldOptions(
            Math.Max(64, requirements.EntityCount),
            requirements.IdentityOptions,
            _runtimeProfile));
        try
        {
            GameContent content = CompileContent(world);
            (ComponentLookup<GameSimulationState> simulation, GameReplicationBindings? replication) =
                PrepareSimulation(world, _runtimeProfile);
            return new GameStaging(
                world,
                new InventoryGenerationState(world, content, limits ?? _inventoryLimits),
                content,
                CreateWorldState(world, content),
                simulation,
                replication);
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    public GameStaging CreateFresh(
        int entityCapacity,
        PersistenceIdentityOptions identity,
        InventoryLimits? limits = null)
    {
        var world = new EcsWorld(Schema, CreateWorldOptions(entityCapacity, identity, _runtimeProfile));
        try
        {
            GameContent content = CompileContent(world);
            (ComponentLookup<GameSimulationState> simulation, GameReplicationBindings? replication) =
                PrepareSimulation(world, _runtimeProfile);
            return new GameStaging(
                world,
                new InventoryGenerationState(world, content, limits ?? _inventoryLimits),
                content,
                CreateWorldState(world, content),
                simulation,
                replication);
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    public bool TryCreatePlayable(EcsWorld world, ParticipantId participant, out Entity entity)
    {
        entity = Entity.Null;
        return _contentByWorld.TryGetValue(world, out GameContent? content) &&
            world.TryInstantiate(content.Character, out entity);
    }

    public bool IsPlayable(EcsWorld world, in CharacterRecord character, Entity resolvedEntity) =>
        world.IsAlive(resolvedEntity) &&
        world.TryGetPrefabProvenance(resolvedEntity, out PrefabKey prefab) &&
        prefab == character.PrefabKey;

    public bool TryGetContent(EcsWorld world, out GameContent content) =>
        _contentByWorld.TryGetValue(world, out content!);

    public static EcsWorldOptions CreateWorldOptions(
        int entityCapacity = 4_096,
        PersistenceIdentityOptions? identity = null,
        GameRuntimeProfile runtimeProfile = GameRuntimeProfile.Local) => new(
        new EntityCapacity(entityCapacity),
        maximumArchetypes: 32,
        maximumPreparedPlans: 40,
        maximumStructuralCommands: runtimeProfile switch
        {
            GameRuntimeProfile.Local => 0,
            GameRuntimeProfile.Networked => NetworkCommandCapacity,
            _ => throw new ArgumentOutOfRangeException(nameof(runtimeProfile)),
        },
        scheduler: new EcsSchedulerOptions(
            maximumSystems: 8,
            maximumDependencies: 16,
            maximumWorkItems: 16,
            maximumStructuralSegments: 16,
            backgroundWorkerCount: 0),
        lifecycleRecordCapacity: runtimeProfile switch
        {
            GameRuntimeProfile.Local => SpatialLifecycleRecordCapacity,
            GameRuntimeProfile.Networked => SpatialLifecycleRecordCapacity,
            _ => throw new ArgumentOutOfRangeException(nameof(runtimeProfile)),
        },
        persistence: identity ?? new PersistenceIdentityOptions(new PersistentWorldId(0x7007)));

    private GameContent CompileContent(EcsWorld world)
    {
        GameContent content = GameContent.Compile(
            world,
            new ContentCompilationOptions(
                ChunkLayoutOptions.KiB16,
                maximumChunksPerNewArchetype: _maximumChunksPerContentArchetype));
        _contentByWorld.Add(world, content);
        return content;
    }

    private GameWorldState CreateWorldState(EcsWorld world, GameContent content)
    {
        BoundWorldPack? bound = null;
        if (_baseWorldPack is not null &&
            !_baseWorldPack.TryBind(content.World, out bound, out WorldPackBindingError error))
            throw new InvalidOperationException($"WorldPack could not bind to compiled game content: {error}.");
        return new GameWorldState(world, bound);
    }

    private static (ComponentLookup<GameSimulationState>, GameReplicationBindings?) PrepareSimulation(
        EcsWorld world,
        GameRuntimeProfile runtimeProfile)
    {
        WriteJobPlan<GameSimulationState> plan = world.PrepareWriteJob<GameSimulationState>();
        ComponentLookup<GameSimulationState> lookup = world.PrepareLookup<GameSimulationState>();
        GameReplicationBindings? replication = null;
        if (runtimeProfile == GameRuntimeProfile.Networked)
        {
            ReadJobPlan<GameSimulationState> baseline = world.PrepareReadJob<GameSimulationState>();
            ChangedRowsJobPlan<GameSimulationState, GameSimulationState> changes =
                world.PrepareChangedRowsJob<GameSimulationState, GameSimulationState>();
            var commands = new GameSimulationCommandRuntime(NetworkCommandCapacity, lookup);
            ComponentId simulationId = GameSchema.GetComponentId<GameSimulationState>();
            world.RegisterRangeSystem<GameSimulationCommandRuntime, GameSimulationCommandJob>(
                new SystemDefinition(
                    "game.replication.authoritative-ingress",
                    SystemStage.Input,
                    executionPolicy: SystemExecutionPolicy.Serial,
                    additionalReads: [simulationId],
                    additionalWrites: [simulationId],
                    structuralComponents: [simulationId]),
                commands);
            replication = new GameReplicationBindings(
                world.Id,
                baseline,
                changes,
                commands,
                simulationId);
        }
        world.RegisterWriteSystem<
            GameSimulationState,
            GameSimulationSystemRuntime,
            GameSimulationJob>(
            new SystemDefinition(
                "game.simulation.tick-marker",
                SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Serial),
            plan,
            new GameSimulationSystemRuntime());
        return (lookup, replication);
    }
}
