using UltraSim.ECS;
using UltraSim.Game.Inventory;

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
[EcsComponent("game.activity.simulation-state", PersistenceId = 700602)]
public struct GameSimulationState : IComponent<GameSimulationState>
{
    public int TickMarker;
}

[EcsSchema(
    typeof(InventoryContainer),
    typeof(GameSimulationState),
    typeof(InventoryItem),
    typeof(InventoryContainerOwner),
    typeof(GameCharacter),
    typeof(InventoryEquipCapability),
    typeof(InventoryStackPolicy))]
public static partial class GameSchema
{
}
