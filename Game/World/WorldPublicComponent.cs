using System.Runtime.InteropServices;
using UltraSim.ECS;

namespace UltraSim.Game.World;

[Flags]
public enum WorldPublicStateFlags : ushort
{
    None = 0,
    Visible = 1 << 0,
}

/// <summary>Game-owned public projection semantics read from committed ECS authority.</summary>
public readonly record struct WorldPublicDescriptor(
    uint PrefabId,
    WorldYaw Orientation,
    WorldPublicStateFlags Flags)
{
    public bool IsValid => PrefabId != 0 &&
        (Flags & ~WorldPublicStateFlags.Visible) == 0;
}

/// <summary>
/// Explicit persisted eligibility and minimal public semantics for one dynamic world entity.
/// Presence of this component permits publication only when a valid world position is also present.
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
