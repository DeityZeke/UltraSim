using System.Runtime.InteropServices;

namespace UltraSim.ECS;

internal enum StructuralCommandKind : byte
{
    CreateDefault = 1,
    Destroy = 2,
    Transition = 3,
    TemporaryHole = 4,
    AddComponent = 5,
    RemoveComponent = 6,
}

internal enum StructuralCommandTargetKind : byte
{
    None = 0,
    Entity = 1,
    Deferred = 2,
}

/// <summary>
/// Discriminated identity for one homogeneous structural run. Numeric route IDs
/// are intentionally local to their command namespace and are never sufficient
/// by themselves.
/// </summary>
internal readonly struct StructuralRunKey
{
    internal StructuralRunKey(in StructuralCommandRecord command)
    {
        Kind = command.Kind;
        Phase = command.TransitionPhase;
        Argument = command.Argument;
    }

    internal StructuralCommandKind Kind { get; }
    internal StructuralTransitionPhase Phase { get; }
    internal uint Argument { get; }

    internal bool Matches(in StructuralCommandRecord command) =>
        command.Kind == Kind &&
        command.TransitionPhase == Phase &&
        command.Argument == Argument;
}

/// <summary>Fixed unmanaged union stored by the structural command arena.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 24)]
internal readonly struct StructuralCommandRecord
{
    private readonly StructuralCommandKind _kind;
    private readonly StructuralCommandTargetKind _targetKind;
    private readonly StructuralTransitionPhase _transitionPhase;
    private readonly byte _reserved;
    private readonly uint _argument;
    private readonly uint _target0;
    private readonly uint _target1;
    private readonly uint _target2;
    private readonly uint _target3;

    private StructuralCommandRecord(
        StructuralCommandKind kind,
        StructuralCommandTargetKind targetKind,
        StructuralTransitionPhase transitionPhase,
        uint argument,
        uint target0,
        uint target1,
        uint target2,
        uint target3)
    {
        _kind = kind;
        _targetKind = targetKind;
        _transitionPhase = transitionPhase;
        _reserved = 0;
        _argument = argument;
        _target0 = target0;
        _target1 = target1;
        _target2 = target2;
        _target3 = target3;
    }

    internal StructuralCommandKind Kind => _kind;

    internal StructuralCommandTargetKind TargetKind => _targetKind;

    internal StructuralTransitionPhase TransitionPhase => _transitionPhase;

    internal uint Argument => _argument;

    internal static StructuralCommandRecord CreateDefault(ArchetypeId archetypeId) => new(
        StructuralCommandKind.CreateDefault,
        StructuralCommandTargetKind.None,
        default,
        archetypeId.Value,
        0,
        0,
        0,
        0);

    internal static StructuralCommandRecord Destroy(Entity entity) => new(
        StructuralCommandKind.Destroy,
        StructuralCommandTargetKind.Entity,
        default,
        0,
        entity.World.Value,
        entity.Index,
        entity.Generation,
        0);

    internal static StructuralCommandRecord Destroy(DeferredEntity entity) => new(
        StructuralCommandKind.Destroy,
        StructuralCommandTargetKind.Deferred,
        default,
        0,
        entity.BufferId,
        entity.Epoch,
        entity.CommandIndex,
        0);

    internal static StructuralCommandRecord Transition(
        Entity entity,
        uint transitionId,
        StructuralTransitionPhase phase) => new(
        StructuralCommandKind.Transition,
        StructuralCommandTargetKind.Entity,
        phase,
        transitionId,
        entity.World.Value,
        entity.Index,
        entity.Generation,
        0);

    internal static StructuralCommandRecord Transition(
        DeferredEntity entity,
        uint transitionId,
        StructuralTransitionPhase phase) => new(
        StructuralCommandKind.Transition,
        StructuralCommandTargetKind.Deferred,
        phase,
        transitionId,
        entity.BufferId,
        entity.Epoch,
        entity.CommandIndex,
        0);

    internal static StructuralCommandRecord AddComponent(
        Entity entity,
        uint componentRouteId,
        uint payloadOffset) => new(
        StructuralCommandKind.AddComponent,
        StructuralCommandTargetKind.Entity,
        StructuralTransitionPhase.Add,
        componentRouteId,
        entity.World.Value,
        entity.Index,
        entity.Generation,
        payloadOffset);

    internal static StructuralCommandRecord AddComponent(
        DeferredEntity entity,
        uint componentRouteId,
        uint payloadOffset) => new(
        StructuralCommandKind.AddComponent,
        StructuralCommandTargetKind.Deferred,
        StructuralTransitionPhase.Add,
        componentRouteId,
        entity.BufferId,
        entity.Epoch,
        entity.CommandIndex,
        payloadOffset);

    internal static StructuralCommandRecord RemoveComponent(
        Entity entity,
        uint componentRouteId) => new(
        StructuralCommandKind.RemoveComponent,
        StructuralCommandTargetKind.Entity,
        StructuralTransitionPhase.Remove,
        componentRouteId,
        entity.World.Value,
        entity.Index,
        entity.Generation,
        0);

    internal static StructuralCommandRecord RemoveComponent(
        DeferredEntity entity,
        uint componentRouteId) => new(
        StructuralCommandKind.RemoveComponent,
        StructuralCommandTargetKind.Deferred,
        StructuralTransitionPhase.Remove,
        componentRouteId,
        entity.BufferId,
        entity.Epoch,
        entity.CommandIndex,
        0);

    /// <summary>
    /// Reuses a consumed live-destroy command slot as barrier-local hole metadata.
    /// Create-token command records remain untouched after playback.
    /// </summary>
    internal static StructuralCommandRecord TemporaryHole(
        ArchetypeId archetypeId,
        ChunkRow row,
        int fixedRowBytes) => new(
        StructuralCommandKind.TemporaryHole,
        StructuralCommandTargetKind.None,
        default,
        archetypeId.Value,
        row.ChunkIndex,
        checked((uint)row.Row),
        checked((uint)fixedRowBytes),
        0);

    internal Entity GetEntityTarget() => new(
        new WorldId(_target0),
        _target1,
        _target2);

    internal bool MatchesDeferredTarget(uint bufferId, uint epoch) =>
        _target0 == bufferId && _target1 == epoch;

    internal uint DeferredCommandIndex => _target2;

    internal uint PayloadOffset => _target3;

    internal StructuralCommandRecord WithPayloadOffset(uint payloadOffset) => new(
        _kind,
        _targetKind,
        _transitionPhase,
        _argument,
        _target0,
        _target1,
        _target2,
        payloadOffset);

    internal StructuralCommandRecord RemapDeferredTarget(
        uint bufferId,
        uint epoch,
        uint commandIndex)
    {
        if (_targetKind != StructuralCommandTargetKind.Deferred)
            throw new InvalidOperationException("Only a deferred structural target can be remapped.");
        return new StructuralCommandRecord(
            _kind,
            _targetKind,
            _transitionPhase,
            _argument,
            bufferId,
            epoch,
            commandIndex,
            _target3);
    }

    internal ArchetypeId TemporaryHoleArchetypeId => new(_argument);

    internal ChunkRow TemporaryHoleRow => new(_target0, checked((int)_target1));

    internal int TemporaryHoleFixedRowBytes => checked((int)_target2);

}
