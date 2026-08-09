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
    private const uint AddPhaseMask = 1U << 31;
    private const uint TransitionIdMask = ~AddPhaseMask;

    private readonly uint _worldId;
    private readonly uint _transitionIdAndPhase;
    private readonly uint _sourceArchetypeId;
    private readonly uint _destinationArchetypeId;

    internal ArchetypeTransition(
        WorldId worldId,
        uint transitionId,
        ArchetypeHandle source,
        ArchetypeHandle destination,
        StructuralTransitionPhase phase)
    {
        if (!worldId.IsValid || transitionId == 0 || transitionId > TransitionIdMask ||
            source.World != worldId || destination.World != worldId)
        {
            throw new ArgumentException("A valid world-owned prepared transition is required.");
        }
        if (phase is not (StructuralTransitionPhase.Remove or StructuralTransitionPhase.Add))
            throw new ArgumentOutOfRangeException(nameof(phase));

        _worldId = worldId.Value;
        _transitionIdAndPhase = phase == StructuralTransitionPhase.Add
            ? transitionId | AddPhaseMask
            : transitionId;
        _sourceArchetypeId = source.Id.Value;
        _destinationArchetypeId = destination.Id.Value;
    }

    public bool IsValid =>
        _worldId != 0 &&
        (_transitionIdAndPhase & TransitionIdMask) != 0 &&
        _sourceArchetypeId != 0 &&
        _destinationArchetypeId != 0;

    public ArchetypeHandle Source => IsValid
        ? new ArchetypeHandle(new WorldId(_worldId), new ArchetypeId(_sourceArchetypeId))
        : ArchetypeHandle.Invalid;

    public ArchetypeHandle Destination => IsValid
        ? new ArchetypeHandle(new WorldId(_worldId), new ArchetypeId(_destinationArchetypeId))
        : ArchetypeHandle.Invalid;

    internal WorldId WorldId => new(_worldId);

    internal uint TransitionId => _transitionIdAndPhase & TransitionIdMask;

    internal StructuralTransitionPhase Phase =>
        (_transitionIdAndPhase & AddPhaseMask) != 0
            ? StructuralTransitionPhase.Add
            : StructuralTransitionPhase.Remove;

    internal ArchetypeId SourceId => new(_sourceArchetypeId);

    internal ArchetypeId DestinationId => new(_destinationArchetypeId);

    public bool Equals(ArchetypeTransition other) =>
        _worldId == other._worldId &&
        _transitionIdAndPhase == other._transitionIdAndPhase &&
        _sourceArchetypeId == other._sourceArchetypeId &&
        _destinationArchetypeId == other._destinationArchetypeId;

    public override bool Equals(object? obj) => obj is ArchetypeTransition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        _worldId,
        _transitionIdAndPhase,
        _sourceArchetypeId,
        _destinationArchetypeId);

    public static bool operator ==(ArchetypeTransition left, ArchetypeTransition right) => left.Equals(right);

    public static bool operator !=(ArchetypeTransition left, ArchetypeTransition right) => !left.Equals(right);
}
