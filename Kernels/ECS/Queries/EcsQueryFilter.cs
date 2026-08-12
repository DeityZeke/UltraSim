namespace UltraSim.ECS;

/// <summary>Opaque world-bound topology selection for generated chunk-job preparation.</summary>
/// <remarks>
/// A filter is prepared during cold world construction and becomes immutable.
/// Required and excluded components select archetypes only: they are not job
/// data columns, read/write capabilities, or scheduler hazards. Storage-free
/// tags belong here rather than in fake data columns. Duplicate terms are
/// canonicalized; contradictory required/excluded terms are rejected.
/// </remarks>
public readonly struct EcsQueryFilter
{
    private readonly ArchetypeQueryFilter _filter;

    internal EcsQueryFilter(ArchetypeQueryFilter filter)
    {
        _filter = filter;
    }

    /// <summary>Gets the world that prepared this selection.</summary>
    public WorldId World => _filter.WorldId;

    /// <summary>Gets whether this value was prepared by a world.</summary>
    public bool IsValid => _filter.IsValid;

    internal ArchetypeQueryFilter GetFilter(WorldId expectedWorld, string parameterName)
    {
        if (!_filter.IsValid)
            throw new ArgumentException("The query filter is invalid or default initialized.", parameterName);
        if (_filter.WorldId != expectedWorld)
            throw new ArgumentException("The query filter belongs to a different world.", parameterName);
        return _filter;
    }
}
