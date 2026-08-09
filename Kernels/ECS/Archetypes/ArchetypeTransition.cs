using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Compact checked handle for one world-owned prepared transition.</summary>
/// <remarks>
/// The handle is unmanaged and retains no world, store, arena, or transfer array.
/// It is transient runtime routing data and must not be persisted.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
public readonly struct ArchetypeTransition : IEquatable<ArchetypeTransition>
{
    private readonly uint _worldId;
    private readonly uint _transitionId;
    private readonly uint _sourceArchetypeId;
    private readonly uint _destinationArchetypeId;

    internal ArchetypeTransition(
        WorldId worldId,
        uint transitionId,
        ArchetypeHandle source,
        ArchetypeHandle destination)
    {
        if (!worldId.IsValid || transitionId == 0 ||
            source.World != worldId || destination.World != worldId)
        {
            throw new ArgumentException("A valid world-owned prepared transition is required.");
        }

        _worldId = worldId.Value;
        _transitionId = transitionId;
        _sourceArchetypeId = source.Id.Value;
        _destinationArchetypeId = destination.Id.Value;
    }

    public bool IsValid =>
        _worldId != 0 &&
        _transitionId != 0 &&
        _sourceArchetypeId != 0 &&
        _destinationArchetypeId != 0;

    public ArchetypeHandle Source => IsValid
        ? new ArchetypeHandle(new WorldId(_worldId), new ArchetypeId(_sourceArchetypeId))
        : ArchetypeHandle.Invalid;

    public ArchetypeHandle Destination => IsValid
        ? new ArchetypeHandle(new WorldId(_worldId), new ArchetypeId(_destinationArchetypeId))
        : ArchetypeHandle.Invalid;

    internal WorldId WorldId => new(_worldId);

    internal uint TransitionId => _transitionId;

    internal ArchetypeId SourceId => new(_sourceArchetypeId);

    internal ArchetypeId DestinationId => new(_destinationArchetypeId);

    public bool Equals(ArchetypeTransition other) =>
        _worldId == other._worldId &&
        _transitionId == other._transitionId &&
        _sourceArchetypeId == other._sourceArchetypeId &&
        _destinationArchetypeId == other._destinationArchetypeId;

    public override bool Equals(object? obj) => obj is ArchetypeTransition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        _worldId,
        _transitionId,
        _sourceArchetypeId,
        _destinationArchetypeId);

    public static bool operator ==(ArchetypeTransition left, ArchetypeTransition right) => left.Equals(right);

    public static bool operator !=(ArchetypeTransition left, ArchetypeTransition right) => !left.Equals(right);
}
