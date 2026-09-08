using UltraSim.ECS;
using UltraSim.Game.Inventory;
using UltraSim.Game.World;

namespace UltraSim.Game.Composition;

/// <summary>Minimal persistent game-owned character identity for S0.</summary>
[EcsComponent("game.character", PersistenceId = 700601)]
public struct GameCharacter : IComponent<GameCharacter>
{
    public int Marker;
}

/// <summary>
/// Unrelated simulation component deliberately included in the one closed game
/// schema so Inventory can never assume locally assigned component IDs.
/// </summary>
[EcsComponent("game.activity.simulation-state", Version = 2, PersistenceId = 700602)]
public struct GameSimulationState : IComponent<GameSimulationState>
{
    public int TickMarker;
    /// <summary>Step-relative diagnostic velocity controlled by N0.6 input.</summary>
    public int InputVelocity;
}

/// <summary>Persisted state of the concrete T0.10 cabin-door interaction.</summary>
[EcsComponent("game.world.cabin-door", PersistenceId = 700605)]
public struct GameCabinDoorState : IComponent<GameCabinDoorState>
{
    public int IsOpen;
}

/// <summary>Finite authoritative presentation/gameplay vocabulary for one rolled weapon.</summary>
public enum GameWeaponElement : byte
{
    None = 0,
    Fire = 1,
    Cold = 2,
    Lightning = 3,
}

/// <summary>
/// Persistent one-time result of a Game-owned weapon spawn recipe. Restore reads these values;
/// it never reruns the recipe or depends on the implementation that originally rolled them.
/// </summary>
[EcsComponent("game.item.weapon-instance", PersistenceId = 700608)]
public struct GameWeaponInstance : IComponent<GameWeaponInstance>
{
    public ushort DamageMinimum;
    public ushort DamageMaximum;
    public ushort MaximumDurability;
    public ushort CurrentDurability;
    public byte Quality;
    public GameWeaponElement Element;
    public uint SpawnOrdinal;

    public readonly bool IsCanonical() => DamageMinimum != 0 && DamageMaximum >= DamageMinimum &&
        MaximumDurability != 0 && CurrentDurability <= MaximumDurability &&
        Quality is >= 1 and <= 100 &&
        Element is >= GameWeaponElement.None and <= GameWeaponElement.Lightning;
}

/// <summary>Persistent origin of a spawned item; identity is not ownership.</summary>
[EcsComponent("game.item.spawn-origin", PersistenceId = 700609)]
public struct GameSpawnedItem : IComponent<GameSpawnedItem>
{
    [PersistenceEntityReference]
    public Entity Spawner;
}

/// <summary>
/// Persistent respawn lineage. ActiveItem becomes null permanently for one pickup; a later ordinal
/// creates a new entity instead of recalling or rerolling the prior instance.
/// </summary>
[EcsComponent("game.item.spawner", PersistenceId = 700610)]
public struct GameItemSpawnerState : IComponent<GameItemSpawnerState>
{
    [PersistenceEntityReference]
    public Entity ActiveItem;
    public uint NextSpawnOrdinal;
    public int RemainingRespawnTicks;
}

[EcsSchema(
    typeof(InventoryContainer),
    typeof(GameSimulationState),
    typeof(InventoryItem),
    typeof(InventoryContainerOwner),
    typeof(GameCharacter),
    typeof(WorldPositionComponent),
    typeof(WorldPublicComponent),
    typeof(GameCabinDoorState),
    typeof(GameWeaponInstance),
    typeof(GameSpawnedItem),
    typeof(GameItemSpawnerState),
    typeof(InventoryEquipCapability),
    typeof(InventoryStackPolicy),
    typeof(InventoryGridPlacement),
    typeof(InventoryGridFootprint))]
public static partial class GameSchema
{
}
