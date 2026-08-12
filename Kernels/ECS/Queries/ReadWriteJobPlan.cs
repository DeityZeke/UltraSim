using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Opaque world-owned prepared chunk plan with one read and one write.</summary>
/// <remarks>
/// Prepare before sealing. The plan selects matching archetypes by composition
/// and binds contiguous columns once; it is not a random-access route.
/// </remarks>
public readonly struct ReadWriteJobPlan<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    private readonly ArchetypeReadWriteQueryPlan<NativeChunkArena, TRead, TWrite>? _plan;

    internal ReadWriteJobPlan(ArchetypeReadWriteQueryPlan<NativeChunkArena, TRead, TWrite> plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    public bool IsValid => _plan is not null;
    public WorldId WorldId => _plan?.WorldId ?? default;
    public int MatchedArchetypeCount => _plan?.MatchedArchetypeCount ?? 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ArchetypeReadWriteQueryPlan<NativeChunkArena, TRead, TWrite> GetPlan() =>
        _plan ?? throw new ArgumentException(
            "The generated job plan is invalid or default initialized.",
            "plan");
}
