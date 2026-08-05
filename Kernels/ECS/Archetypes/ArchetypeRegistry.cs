using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Fixed-capacity, instance-owned canonical archetype registry and row-store owner.
/// </summary>
/// <remarks>
/// Registration is a cold, single-owner bootstrap operation. <see cref="Seal"/>
/// closes the allocation boundary; signature lookup and dense-ID routing remain
/// allocation-free afterward. Concurrent immutable lookup is permitted only after
/// sealing and publication through the owning world barrier.
/// </remarks>
internal sealed class ArchetypeRegistry<TArena> : IDisposable
    where TArena : class, IChunkArenaBackend
{
    private const int MaximumSupportedArchetypes = 1 << 29;

    private readonly ComponentSchema _schema;
    private readonly ArchetypeStorage<TArena>?[] _storage;
    private readonly int[] _lookup;
    private Func<ChunkArenaCapacity, TArena>? _arenaFactory;
    private int _count;
    private bool _sealed;
    private bool _disposed;

    internal ArchetypeRegistry(
        ComponentSchema schema,
        int maximumArchetypes,
        Func<ChunkArenaCapacity, TArena> arenaFactory)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(arenaFactory);
        if (maximumArchetypes <= 0 || maximumArchetypes > MaximumSupportedArchetypes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumArchetypes),
                maximumArchetypes,
                $"Maximum archetypes must be between 1 and {MaximumSupportedArchetypes}.");
        }

        _schema = schema;
        _storage = new ArchetypeStorage<TArena>[maximumArchetypes];
        _lookup = new int[GetLookupSize(maximumArchetypes)];
        _arenaFactory = arenaFactory;
    }

    internal ComponentSchema Schema => _schema;

    internal int Count => _count;

    internal int MaximumArchetypes => _storage.Length;

    internal int LookupCapacity => _lookup.Length;

    internal bool IsSealed => _sealed;

    internal bool IsDisposed => _disposed;

    internal ArchetypeId Register(
        ReadOnlySpan<ComponentId> canonicalSignature,
        ChunkLayoutOptions layoutOptions,
        int maximumChunks)
    {
        if (!TryRegister(canonicalSignature, layoutOptions, maximumChunks, out ArchetypeId id))
        {
            throw new InvalidOperationException(
                $"Archetype capacity {_storage.Length} is exhausted.");
        }

        return id;
    }

    internal bool TryRegister(
        ReadOnlySpan<ComponentId> canonicalSignature,
        ChunkLayoutOptions layoutOptions,
        int maximumChunks,
        out ArchetypeId id)
    {
        ThrowIfDisposed();
        if (_sealed)
            throw new InvalidOperationException("The archetype registry is sealed.");
        if (!layoutOptions.IsValid)
            throw new ArgumentException("Chunk layout options are invalid or default initialized.", nameof(layoutOptions));
        if (maximumChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunks));

        ValidateCanonicalSignature(canonicalSignature);
        ulong hash = ArchetypeSignatureHash.Compute(canonicalSignature);
        if (TryFind(hash, canonicalSignature, out int lookupSlot, out ArchetypeStorage<TArena>? existing))
        {
            if (existing.Layout.Options != layoutOptions ||
                existing.Store.GetSnapshot().MaximumChunks != maximumChunks)
            {
                throw new ArgumentException(
                    "An existing archetype was registered with different chunk geometry or capacity.",
                    nameof(canonicalSignature));
            }

            id = existing.Id;
            return true;
        }

        if (_count == _storage.Length)
        {
            id = ArchetypeId.Invalid;
            return false;
        }

        var columns = new ComponentColumnLayout[canonicalSignature.Length];
        ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(
            _schema,
            canonicalSignature,
            layoutOptions,
            columns);
        var signatureCopy = canonicalSignature.ToArray();
        ChunkArenaCapacity arenaCapacity = ChunkArenaCapacity.FromLayout(layout, maximumChunks);
        TArena? arena = null;
        ArchetypeChunkStore<TArena>? store = null;
        try
        {
            Func<ChunkArenaCapacity, TArena> factory = _arenaFactory
                ?? throw new InvalidOperationException("The archetype registry allocation boundary is closed.");
            arena = factory(arenaCapacity)
                ?? throw new InvalidOperationException("The chunk arena factory returned null.");
            if (arena.Capacity != arenaCapacity)
            {
                throw new ArgumentException(
                    "The chunk arena factory returned storage with different capacity than requested.",
                    "arenaFactory");
            }
            store = new ArchetypeChunkStore<TArena>(arena, layout, columns);
            arena = null; // Ownership transferred to the store.

            id = new ArchetypeId(checked((uint)_count + 1));
            var registered = new ArchetypeStorage<TArena>(id, hash, signatureCopy, store);
            _storage[_count] = registered;
            _lookup[lookupSlot] = _count + 1;
            _count++;
            store = null; // Ownership transferred to the registry record.
            return true;
        }
        catch
        {
            store?.Dispose();
            arena?.Dispose();
            throw;
        }
    }

    internal bool TryGetId(ReadOnlySpan<ComponentId> canonicalSignature, out ArchetypeId id)
    {
        ThrowIfDisposed();
        ValidateCanonicalSignature(canonicalSignature);
        ulong hash = ArchetypeSignatureHash.Compute(canonicalSignature);
        if (TryFind(hash, canonicalSignature, out _, out ArchetypeStorage<TArena>? storage))
        {
            id = storage.Id;
            return true;
        }

        id = ArchetypeId.Invalid;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ArchetypeStorage<TArena> GetStorage(ArchetypeId id)
    {
        ThrowIfDisposed();
        if (!id.IsValid)
            throw new ArgumentOutOfRangeException(nameof(id));

        uint index = id.Value - 1;
        if (index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(id));

        return _storage[index]
            ?? throw new InvalidOperationException("A dense archetype ID has no registered storage.");
    }

    internal bool TryGetStorage(ArchetypeId id, out ArchetypeStorage<TArena>? storage)
    {
        ThrowIfDisposed();
        if (!id.IsValid)
        {
            storage = null;
            return false;
        }

        uint index = id.Value - 1;
        if (index >= (uint)_count)
        {
            storage = null;
            return false;
        }

        storage = _storage[index];
        return storage is not null;
    }

    internal ArchetypeTransitionPlan<TArena> PrepareTransition(
        ArchetypeId sourceId,
        ArchetypeId destinationId)
    {
        ThrowIfDisposed();
        if (_sealed)
            throw new InvalidOperationException("Transition plans must be prepared before the archetype registry is sealed.");

        return ArchetypeTransitionPlan<TArena>.Create(
            GetStorage(sourceId),
            GetStorage(destinationId));
    }

    internal void Seal()
    {
        ThrowIfDisposed();
        if (_sealed)
            return;

        _sealed = true;
        _arenaFactory = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        for (int i = 0; i < _count; i++)
            _storage[i]?.Store.Dispose();

        _arenaFactory = null;
        _disposed = true;
    }

    private bool TryFind(
        ulong hash,
        ReadOnlySpan<ComponentId> signature,
        out int lookupSlot,
        [NotNullWhen(true)]
        out ArchetypeStorage<TArena>? storage)
    {
        int mask = _lookup.Length - 1;
        int slot = ArchetypeSignatureHash.GetInitialSlot(hash, mask);
        while (true)
        {
            int entry = _lookup[slot];
            if (entry == 0)
            {
                lookupSlot = slot;
                storage = null;
                return false;
            }

            ArchetypeStorage<TArena> candidate = _storage[entry - 1]
                ?? throw new InvalidOperationException("The archetype lookup table contains an unpublished entry.");
            if (candidate.SignatureHash == hash && candidate.Matches(signature))
            {
                lookupSlot = slot;
                storage = candidate;
                return true;
            }

            slot = (slot + 1) & mask;
        }
    }

    private void ValidateCanonicalSignature(ReadOnlySpan<ComponentId> signature)
    {
        ComponentId previous = ComponentId.Invalid;
        for (int i = 0; i < signature.Length; i++)
        {
            ComponentId current = signature[i];
            if (!current.IsValid || current.Value >= _schema.ComponentCount)
            {
                throw new ArgumentException(
                    "An archetype signature contains a component outside this schema.",
                    nameof(signature));
            }
            if (i != 0 && current.Value <= previous.Value)
            {
                throw new ArgumentException(
                    "Archetype signatures must be unique and in ascending component-ID order.",
                    nameof(signature));
            }

            previous = current;
        }
    }

    private static int GetLookupSize(int maximumArchetypes)
    {
        int required = checked(maximumArchetypes * 2);
        int size = 2;
        while (size < required)
            size <<= 1;
        return size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
