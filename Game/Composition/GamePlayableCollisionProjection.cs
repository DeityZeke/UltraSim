using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Composition;

/// <summary>
/// Small generation-owned projection from the current playable Game state into effective physical
/// blocking bounds. It keeps cabin/chest meaning above the Simple Physics resolver.
/// </summary>
internal sealed class GamePlayableCollisionProjection
{
    internal const int MaximumDynamicBlockers = 16;

    private readonly WorldId _worldId;
    private readonly ComponentLookup<GameCabinDoorState> _door;
    private readonly Entity[] _blockers = new Entity[MaximumDynamicBlockers];
    private readonly WorldStaticBounds[] _effectiveBounds =
        new WorldStaticBounds[MaximumDynamicBlockers];
    private int _blockerCount;

    internal GamePlayableCollisionProjection(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _worldId = world.Id;
        _door = world.PrepareLookup<GameCabinDoorState>();
    }

    internal long ReservedBytes =>
        (long)_blockers.Length * Unsafe.SizeOf<Entity>() +
        (long)_effectiveBounds.Length * Unsafe.SizeOf<WorldStaticBounds>();

    internal int RegisteredBlockerCount => _blockerCount;

    internal void Register(
        EcsWorld world,
        GameWorldState worldState,
        Entity blocker)
    {
        EnsureWorld(world, worldState);
        if (!world.IsAlive(blocker) ||
            !worldState.TryReadPosition(world, blocker, out _) ||
            !worldState.TryReadPublic(world, blocker, out WorldPublicDescriptor descriptor) ||
            descriptor.PrefabId is not (
                GameWorldPublicPrefabIds.CabinDoor or GameWorldPublicPrefabIds.WorldChest))
        {
            throw new ArgumentException(
                "The entity is not a supported playable-world blocker.", nameof(blocker));
        }

        for (int index = 0; index < _blockerCount; index++)
        {
            if (_blockers[index] == blocker)
                return;
        }

        if (_blockerCount >= _blockers.Length)
            throw new InvalidOperationException("The bounded playable dynamic-blocker set is full.");
        _blockers[_blockerCount++] = blocker;
    }

    /// <summary>
    /// Captures the complete effective dynamic blocking projection for one endpoint query. A
    /// contradictory door state fails closed rather than publishing partial physical truth.
    /// </summary>
    internal bool TryCapture(
        EcsWorld world,
        GameWorldState worldState,
        Entity excludedActor,
        SpatialSpaceId space,
        out ReadOnlySpan<WorldStaticBounds> bounds)
    {
        EnsureWorld(world, worldState);
        int count = 0;
        for (int index = 0; index < _blockerCount; index++)
        {
            Entity blocker = _blockers[index];
            if (blocker == excludedActor ||
                !world.IsAlive(blocker) ||
                !worldState.TryReadLocation(world, blocker, out WorldLocation location) ||
                !worldState.TryReadPublic(world, blocker, out WorldPublicDescriptor descriptor))
            {
                continue;
            }

            if (location.Space != space)
                continue;
            WorldPosition position = location.Position;

            WorldStaticBounds projected;
            switch (descriptor.PrefabId)
            {
                case GameWorldPublicPrefabIds.CabinDoor:
                    if (!world.TryRead(blocker, _door, out GameCabinDoorState door) ||
                        door.IsOpen is not (0 or 1))
                    {
                        bounds = default;
                        return false;
                    }
                    if (door.IsOpen != 0)
                        continue;
                    projected = Bounds(
                        position,
                        halfExtentX: 800,
                        halfExtentZ: 110,
                        height: 2_100);
                    break;
                case GameWorldPublicPrefabIds.WorldChest:
                    projected = Bounds(
                        position,
                        halfExtentX: 600,
                        halfExtentZ: 400,
                        height: 800);
                    break;
                default:
                    continue;
            }

            _effectiveBounds[count++] = projected;
        }

        bounds = _effectiveBounds.AsSpan(0, count);
        return true;
    }

    internal void RebuildAndValidate(EcsWorld world, GameWorldState worldState)
    {
        EnsureWorld(world, worldState);
        Array.Clear(_blockers, 0, _blockerCount);
        Array.Clear(_effectiveBounds);
        _blockerCount = 0;

        for (uint index = 0;
             index < (uint)world.Options.EntityCapacity.MaximumEntities;
             index++)
        {
            if (!world.TryGetEntity(index, out Entity entity))
                continue;
            if (worldState.TryReadPublic(world, entity, out WorldPublicDescriptor publicDescriptor) &&
                publicDescriptor.PrefabId is
                    GameWorldPublicPrefabIds.CabinDoor or GameWorldPublicPrefabIds.WorldChest)
            {
                Register(world, worldState, entity);
            }

            if (!world.TryRead(entity, _door, out GameCabinDoorState state))
                continue;
            if (state.IsOpen is not (0 or 1) ||
                !worldState.TryReadPublic(world, entity, out WorldPublicDescriptor descriptor) ||
                descriptor.PrefabId != GameWorldPublicPrefabIds.CabinDoor ||
                descriptor.Orientation != DoorOrientation(state.IsOpen != 0))
            {
                throw new InvalidOperationException(
                    $"Cabin door {entity} has contradictory persisted/public state.");
            }
        }
    }

    private static WorldYaw DoorOrientation(bool isOpen) =>
        isOpen ? WorldYaw.QuarterTurn : WorldYaw.Zero;

    private static WorldStaticBounds Bounds(
        in WorldPosition position,
        int halfExtentX,
        int halfExtentZ,
        int height) => new(
        position.GlobalXMillimeters - halfExtentX,
        position.YMillimeters,
        position.GlobalZMillimeters - halfExtentZ,
        position.GlobalXMillimeters + halfExtentX + 1L,
        position.YMillimeters + height,
        position.GlobalZMillimeters + halfExtentZ + 1L);

    private void EnsureWorld(EcsWorld world, GameWorldState worldState)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(worldState);
        if (world.Id != _worldId || worldState.WorldId != _worldId)
            throw new ArgumentException("The dynamic collision projection belongs to another generation.");
    }
}
