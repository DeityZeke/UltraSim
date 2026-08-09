namespace UltraSim.ECS;

/// <summary>
/// Cold-built immutable transfer plan between two registered archetype stores.
/// </summary>
internal sealed class ArchetypeTransitionPlan<TArena>
    where TArena : class, IChunkArenaBackend
{
    private readonly ArchetypeColumnTransfer[] _transfers;

    private ArchetypeTransitionPlan(
        ArchetypeStorage<TArena> source,
        ArchetypeStorage<TArena> destination,
        ArchetypeColumnTransfer[] transfers,
        int retainedComponentCount,
        int addedComponentCount,
        int removedComponentCount)
    {
        Source = source;
        Destination = destination;
        _transfers = transfers;
        RetainedComponentCount = retainedComponentCount;
        AddedComponentCount = addedComponentCount;
        RemovedComponentCount = removedComponentCount;
    }

    internal ArchetypeStorage<TArena> Source { get; }

    internal ArchetypeStorage<TArena> Destination { get; }

    internal ReadOnlySpan<ArchetypeColumnTransfer> Transfers => _transfers;

    internal int RetainedComponentCount { get; }

    internal int StatefulTransferCount => _transfers.Length;

    internal int AddedComponentCount { get; }

    internal int RemovedComponentCount { get; }

    internal PreparedArchetypeTransition ToPrepared(WorldId worldId) => new(
        worldId,
        Source.Id,
        Destination.Id,
        _transfers,
        AddedComponentCount == 0
            ? StructuralTransitionPhase.Remove
            : StructuralTransitionPhase.Add);

    internal static ArchetypeTransitionPlan<TArena> Create(
        ArchetypeStorage<TArena> source,
        ArchetypeStorage<TArena> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (source.Id == destination.Id || ReferenceEquals(source, destination))
            throw new ArgumentException("An archetype transition requires distinct source and destination stores.");

        ReadOnlySpan<ComponentId> sourceSignature = source.Signature;
        ReadOnlySpan<ComponentId> destinationSignature = destination.Signature;
        CountChanges(
            sourceSignature,
            destinationSignature,
            out int retained,
            out int added,
            out int removed);

        int stateful = CountStatefulTransfers(source, destination);
        var transfers = new ArchetypeColumnTransfer[stateful];
        int sourceIndex = 0;
        int destinationIndex = 0;
        int transferIndex = 0;
        while (sourceIndex < sourceSignature.Length && destinationIndex < destinationSignature.Length)
        {
            ComponentId sourceId = sourceSignature[sourceIndex];
            ComponentId destinationId = destinationSignature[destinationIndex];
            if (sourceId.Value < destinationId.Value)
            {
                sourceIndex++;
                continue;
            }
            if (destinationId.Value < sourceId.Value)
            {
                destinationIndex++;
                continue;
            }

            int sourceColumnIndex = source.Store.GetColumnIndex(sourceId);
            int destinationColumnIndex = destination.Store.GetColumnIndex(destinationId);
            if (sourceColumnIndex < 0 || destinationColumnIndex < 0)
                throw new InvalidOperationException("A registered archetype signature and physical columns disagree.");

            ComponentColumnLayout sourceColumn = source.Columns[sourceColumnIndex];
            ComponentColumnLayout destinationColumn = destination.Columns[destinationColumnIndex];
            if (sourceColumn.ElementSizeBytes != destinationColumn.ElementSizeBytes ||
                sourceColumn.HasData != destinationColumn.HasData ||
                sourceColumn.HasEnableBits != destinationColumn.HasEnableBits)
            {
                throw new InvalidOperationException("Retained component storage differs between registered archetypes.");
            }

            if (sourceColumn.HasData || sourceColumn.HasEnableBits)
            {
                transfers[transferIndex++] = new ArchetypeColumnTransfer(
                    sourceId,
                    sourceColumnIndex,
                    destinationColumnIndex);
            }

            sourceIndex++;
            destinationIndex++;
        }

        if (transferIndex != transfers.Length)
            throw new InvalidOperationException("Transition transfer accounting is inconsistent.");

        return new ArchetypeTransitionPlan<TArena>(
            source,
            destination,
            transfers,
            retained,
            added,
            removed);
    }

    private static void CountChanges(
        ReadOnlySpan<ComponentId> source,
        ReadOnlySpan<ComponentId> destination,
        out int retained,
        out int added,
        out int removed)
    {
        retained = 0;
        added = 0;
        removed = 0;
        int sourceIndex = 0;
        int destinationIndex = 0;
        while (sourceIndex < source.Length && destinationIndex < destination.Length)
        {
            ComponentId sourceId = source[sourceIndex];
            ComponentId destinationId = destination[destinationIndex];
            if (sourceId.Value < destinationId.Value)
            {
                removed++;
                sourceIndex++;
            }
            else if (destinationId.Value < sourceId.Value)
            {
                added++;
                destinationIndex++;
            }
            else
            {
                retained++;
                sourceIndex++;
                destinationIndex++;
            }
        }

        removed += source.Length - sourceIndex;
        added += destination.Length - destinationIndex;
    }

    private static int CountStatefulTransfers(
        ArchetypeStorage<TArena> source,
        ArchetypeStorage<TArena> destination)
    {
        ReadOnlySpan<ComponentId> sourceSignature = source.Signature;
        ReadOnlySpan<ComponentId> destinationSignature = destination.Signature;
        int sourceIndex = 0;
        int destinationIndex = 0;
        int stateful = 0;
        while (sourceIndex < sourceSignature.Length && destinationIndex < destinationSignature.Length)
        {
            ComponentId sourceId = sourceSignature[sourceIndex];
            ComponentId destinationId = destinationSignature[destinationIndex];
            if (sourceId.Value < destinationId.Value)
            {
                sourceIndex++;
                continue;
            }
            if (destinationId.Value < sourceId.Value)
            {
                destinationIndex++;
                continue;
            }

            int sourceColumnIndex = source.Store.GetColumnIndex(sourceId);
            int destinationColumnIndex = destination.Store.GetColumnIndex(destinationId);
            if (sourceColumnIndex < 0 || destinationColumnIndex < 0)
                throw new InvalidOperationException("A registered archetype signature and physical columns disagree.");
            ComponentColumnLayout sourceColumn = source.Columns[sourceColumnIndex];
            ComponentColumnLayout destinationColumn = destination.Columns[destinationColumnIndex];
            if (sourceColumn.ElementSizeBytes != destinationColumn.ElementSizeBytes ||
                sourceColumn.HasData != destinationColumn.HasData ||
                sourceColumn.HasEnableBits != destinationColumn.HasEnableBits)
            {
                throw new InvalidOperationException("Retained component storage differs between registered archetypes.");
            }
            if (sourceColumn.HasData || sourceColumn.HasEnableBits)
                stateful++;

            sourceIndex++;
            destinationIndex++;
        }

        return stateful;
    }
}
