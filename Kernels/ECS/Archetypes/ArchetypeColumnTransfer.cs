namespace UltraSim.ECS;

/// <summary>Prebound physical-column pair for one retained component.</summary>
internal readonly struct ArchetypeColumnTransfer
{
    internal ArchetypeColumnTransfer(
        ComponentId componentId,
        int sourceColumnIndex,
        int destinationColumnIndex)
    {
        if (!componentId.IsValid)
            throw new ArgumentException("A valid retained component ID is required.", nameof(componentId));
        if (sourceColumnIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceColumnIndex));
        if (destinationColumnIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(destinationColumnIndex));

        ComponentId = componentId;
        SourceColumnIndex = sourceColumnIndex;
        DestinationColumnIndex = destinationColumnIndex;
    }

    internal ComponentId ComponentId { get; }

    internal int SourceColumnIndex { get; }

    internal int DestinationColumnIndex { get; }
}
