using System.Runtime.InteropServices;
using UltraSim.ECS;

namespace UltraSim.Game.World;

[Flags]
public enum WorldPublicStateFlags : ushort
{
    None = 0,
    Visible = 1 << 0,
    /// <summary>
    /// The public entity is an independently selectable item lying in the world. Equipped item
    /// presentation remains Visible but clears this flag so it follows its owner without
    /// masquerading as a second interactable ground object.
    /// </summary>
    GroundItem = 1 << 1,
}

/// <summary>Game-owned public projection semantics read from committed ECS authority.</summary>
public readonly record struct WorldPublicDescriptor(
    uint PrefabId,
    WorldYaw Orientation,
    WorldPublicStateFlags Flags)
{
    public const WorldPublicStateFlags AdmittedFlags =
        WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem;

    public bool IsValid => PrefabId != 0 &&
        (Flags & ~AdmittedFlags) == 0 &&
        ((Flags & WorldPublicStateFlags.GroundItem) == 0 ||
         (Flags & WorldPublicStateFlags.Visible) != 0);
}

/// <summary>
/// Explicit persisted eligibility and minimal public semantics for one dynamic world entity.
/// Presence of this component plus a valid world position permits publication; the Visible flag
/// makes that permitted projection current. Clearing Visible retires the public lifetime without
/// destroying the authoritative entity or its other persisted state.
/// </summary>
[EcsComponent("game.world.public-projection", PersistenceId = 700604)]
[StructLayout(LayoutKind.Sequential, Size = 8)]
public struct WorldPublicComponent : IComponent<WorldPublicComponent>
{
    public uint PrefabId;
    public ushort OrientationUnits;
    public ushort StateFlags;

    public WorldPublicComponent(in WorldPublicDescriptor descriptor)
    {
        if (!descriptor.IsValid)
            throw new ArgumentException("A public projection descriptor must be canonical.", nameof(descriptor));
        PrefabId = descriptor.PrefabId;
        OrientationUnits = descriptor.Orientation.Units;
        StateFlags = (ushort)descriptor.Flags;
    }

    public readonly bool TryGetValue(out WorldPublicDescriptor descriptor)
    {
        descriptor = new WorldPublicDescriptor(
            PrefabId,
            new WorldYaw(OrientationUnits),
            (WorldPublicStateFlags)StateFlags);
        if (descriptor.IsValid)
            return true;
        descriptor = default;
        return false;
    }
}
