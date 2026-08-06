namespace UltraSim.ECS;

/// <summary>Cold-prepared checked route for one immediate archetype transition.</summary>
/// <remarks>
/// The route retains only runtime IDs and immutable physical column mappings, not
/// the world, stores, or arenas it was prepared from.
/// </remarks>
public readonly struct ArchetypeTransition : IEquatable<ArchetypeTransition>
{
    private readonly PreparedArchetypeTransition? _route;

    internal ArchetypeTransition(
        PreparedArchetypeTransition route,
        ArchetypeHandle source,
        ArchetypeHandle destination)
    {
        _route = route;
        Source = source;
        Destination = destination;
    }

    public bool IsValid => _route is not null;

    public ArchetypeHandle Source { get; }

    public ArchetypeHandle Destination { get; }

    internal PreparedArchetypeTransition Route => _route
        ?? throw new InvalidOperationException("The archetype transition is invalid or default initialized.");

    public bool Equals(ArchetypeTransition other) =>
        ReferenceEquals(_route, other._route);

    public override bool Equals(object? obj) => obj is ArchetypeTransition other && Equals(other);

    public override int GetHashCode() => _route?.GetHashCode() ?? 0;

    public static bool operator ==(ArchetypeTransition left, ArchetypeTransition right) => left.Equals(right);

    public static bool operator !=(ArchetypeTransition left, ArchetypeTransition right) => !left.Equals(right);
}
