namespace UltraSim.ECS;

/// <summary>
/// Storage-detached public transition route. Retaining this object after world
/// disposal cannot retain archetype stores, arenas, or entity metadata.
/// </summary>
internal sealed class PreparedArchetypeTransition
{
    private readonly ArchetypeColumnTransfer[] _transfers;

    internal PreparedArchetypeTransition(
        WorldId worldId,
        ArchetypeId sourceId,
        ArchetypeId destinationId,
        ArchetypeColumnTransfer[] transfers,
        StructuralTransitionPhase phase)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        if (!sourceId.IsValid)
            throw new ArgumentException("A valid source archetype is required.", nameof(sourceId));
        if (!destinationId.IsValid || destinationId == sourceId)
            throw new ArgumentException("A distinct valid destination archetype is required.", nameof(destinationId));
        ArgumentNullException.ThrowIfNull(transfers);

        WorldId = worldId;
        SourceId = sourceId;
        DestinationId = destinationId;
        _transfers = transfers;
        Phase = phase;
    }

    internal WorldId WorldId { get; }

    internal ArchetypeId SourceId { get; }

    internal ArchetypeId DestinationId { get; }

    internal StructuralTransitionPhase Phase { get; }

    internal ReadOnlySpan<ArchetypeColumnTransfer> Transfers => _transfers;
}
