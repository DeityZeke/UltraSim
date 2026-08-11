namespace UltraSim.ECS;

/// <summary>
/// Opaque world-owned exact changed-row plan. The watched type is encoded even
/// though execution exposes only the declared read column.
/// </summary>
public readonly struct ChangedRowsJobPlan<TRead, TChanged>
    where TRead : unmanaged, IComponent<TRead>
    where TChanged : unmanaged, IComponent<TChanged>
{
    private readonly ArchetypeChangedReadQueryPlan<NativeChunkArena, TRead>? _plan;

    internal ChangedRowsJobPlan(ArchetypeChangedReadQueryPlan<NativeChunkArena, TRead> plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    public bool IsValid => _plan is not null;

    public WorldId WorldId => _plan?.WorldId ?? default;

    public int MatchedArchetypeCount => _plan?.MatchedArchetypeCount ?? 0;

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal ArchetypeChangedReadQueryPlan<NativeChunkArena, TRead> GetPlan() =>
        _plan ?? throw new ArgumentException(
            "The changed-row job plan is invalid or default initialized.",
            "plan");
}
