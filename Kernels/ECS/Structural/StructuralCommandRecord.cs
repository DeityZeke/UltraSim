using System.Runtime.InteropServices;

namespace UltraSim.ECS;

internal enum StructuralCommandKind : byte
{
    CreateDefault = 1,
    Destroy = 2,
    Transition = 3,
}

internal enum StructuralCommandTargetKind : byte
{
    None = 0,
    Entity = 1,
    Deferred = 2,
}

/// <summary>Fixed unmanaged union stored by the structural command arena.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 24)]
internal readonly struct StructuralCommandRecord
{
    private readonly StructuralCommandKind _kind;
    private readonly StructuralCommandTargetKind _targetKind;
    private readonly ushort _reserved;
    private readonly uint _argument;
    private readonly uint _target0;
    private readonly uint _target1;
    private readonly uint _target2;
    private readonly uint _target3;

    private StructuralCommandRecord(
        StructuralCommandKind kind,
        StructuralCommandTargetKind targetKind,
        uint argument,
        uint target0,
        uint target1,
        uint target2,
        uint target3)
    {
        _kind = kind;
        _targetKind = targetKind;
        _reserved = 0;
        _argument = argument;
        _target0 = target0;
        _target1 = target1;
        _target2 = target2;
        _target3 = target3;
    }

    internal StructuralCommandKind Kind => _kind;

    internal StructuralCommandTargetKind TargetKind => _targetKind;

    internal uint Argument => _argument;

    internal static StructuralCommandRecord CreateDefault(ArchetypeId archetypeId) => new(
        StructuralCommandKind.CreateDefault,
        StructuralCommandTargetKind.None,
        archetypeId.Value,
        0,
        0,
        0,
        0);

    internal static StructuralCommandRecord Destroy(Entity entity) => new(
        StructuralCommandKind.Destroy,
        StructuralCommandTargetKind.Entity,
        0,
        entity.World.Value,
        entity.Index,
        entity.Generation,
        0);

    internal static StructuralCommandRecord Destroy(DeferredEntity entity) => new(
        StructuralCommandKind.Destroy,
        StructuralCommandTargetKind.Deferred,
        0,
        entity.BufferId,
        entity.Epoch,
        entity.CommandIndex,
        0);

    internal static StructuralCommandRecord Transition(Entity entity, uint transitionId) => new(
        StructuralCommandKind.Transition,
        StructuralCommandTargetKind.Entity,
        transitionId,
        entity.World.Value,
        entity.Index,
        entity.Generation,
        0);

    internal static StructuralCommandRecord Transition(DeferredEntity entity, uint transitionId) => new(
        StructuralCommandKind.Transition,
        StructuralCommandTargetKind.Deferred,
        transitionId,
        entity.BufferId,
        entity.Epoch,
        entity.CommandIndex,
        0);

    internal Entity GetEntityTarget() => new(
        new WorldId(_target0),
        _target1,
        _target2);

    internal bool MatchesDeferredTarget(uint bufferId, uint epoch) =>
        _target0 == bufferId && _target1 == epoch;

    internal uint DeferredCommandIndex => _target2;
}
