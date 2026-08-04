namespace UltraSim.ECS;

/// <summary>
/// Immutable signature/layout binding plus the owned mutable rows for one archetype.
/// </summary>
internal sealed class ArchetypeStorage<TArena>
    where TArena : class, IChunkArenaBackend
{
    private readonly ComponentId[] _signature;

    internal ArchetypeStorage(
        ArchetypeId id,
        ulong signatureHash,
        ComponentId[] signature,
        ArchetypeChunkStore<TArena> store)
    {
        if (!id.IsValid)
            throw new ArgumentException("A valid archetype ID is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(store);

        Id = id;
        SignatureHash = signatureHash;
        _signature = signature;
        Store = store;
    }

    internal ArchetypeId Id { get; }

    internal ulong SignatureHash { get; }

    internal ReadOnlySpan<ComponentId> Signature => _signature;

    internal ChunkLayoutPlan Layout => Store.Layout;

    internal ReadOnlySpan<ComponentColumnLayout> Columns => Store.Columns;

    internal ArchetypeChunkStore<TArena> Store { get; }

    internal bool Matches(ReadOnlySpan<ComponentId> signature) =>
        signature.SequenceEqual(_signature);
}
