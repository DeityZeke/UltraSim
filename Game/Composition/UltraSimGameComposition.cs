using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Inventory;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Composition;

public readonly record struct GameStaging(
    EcsWorld World,
    InventoryGenerationState Inventory,
    GameContent Content);

/// <summary>Concrete S0 game composition: one schema, content set, and root.</summary>
public sealed class UltraSimGameComposition : IServerComposition, IAdmissionEntityBinder
{
    private readonly ConditionalWeakTable<EcsWorld, GameContent> _contentByWorld = new();
    private readonly InventoryLimits _inventoryLimits;
    private readonly int _maximumChunksPerContentArchetype;

    public UltraSimGameComposition(
        InventoryLimits? inventoryLimits = null,
        int maximumChunksPerContentArchetype = 16)
    {
        _inventoryLimits = inventoryLimits ?? InventoryLimits.Default;
        _inventoryLimits.Validate();
        if (maximumChunksPerContentArchetype <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunksPerContentArchetype));
        _maximumChunksPerContentArchetype = maximumChunksPerContentArchetype;
        Schema = GameSchema.CreateSchema();
        PersistenceCatalog = GameSchema.CreatePersistenceCatalog();
    }

    public ComponentSchema Schema { get; }
    public PersistenceCatalog PersistenceCatalog { get; }

    public void Configure(ServerGenerationBuilder generation)
    {
        GameContent content = CompileContent(generation.World);
        generation.SetGameState(new GameGenerationState(
            new AccountingStore(),
            new InventoryGenerationState(generation.World, content, _inventoryLimits),
            content));
    }

    public GameStaging CreateStaging(
        PersistenceRestoreRequirements requirements,
        InventoryLimits? limits = null)
    {
        var world = new EcsWorld(requirements.Schema, CreateWorldOptions(
            Math.Max(64, requirements.EntityCount),
            requirements.IdentityOptions));
        try
        {
            GameContent content = CompileContent(world);
            return new GameStaging(
                world,
                new InventoryGenerationState(world, content, limits ?? _inventoryLimits),
                content);
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
        var world = new EcsWorld(Schema, CreateWorldOptions(entityCapacity, identity));
        try
        {
            GameContent content = CompileContent(world);
            return new GameStaging(
                world,
                new InventoryGenerationState(world, content, limits ?? _inventoryLimits),
                content);
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
        PersistenceIdentityOptions? identity = null) => new(
        new EntityCapacity(entityCapacity),
        maximumArchetypes: 32,
        maximumPreparedPlans: 32,
        scheduler: new EcsSchedulerOptions(
            maximumSystems: 8,
            maximumDependencies: 16,
            maximumWorkItems: 16,
            maximumStructuralSegments: 16,
            backgroundWorkerCount: 0),
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
}
