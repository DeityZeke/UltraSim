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

[EcsSchema(
    typeof(InventoryContainer),
    typeof(GameSimulationState),
    typeof(InventoryItem),
    typeof(InventoryContainerOwner),
    typeof(GameCharacter),
    typeof(WorldPositionComponent),
    typeof(WorldPublicComponent),
    typeof(InventoryEquipCapability),
    typeof(InventoryStackPolicy))]
public static partial class GameSchema
{
}
