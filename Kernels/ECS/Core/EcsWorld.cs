using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Instance-owned composition root for one bounded entity pool, archetype registry,
/// and set of prepared structural transitions.
/// </summary>
/// <remarks>
/// Registration is permitted only while created. <see cref="Seal"/> closes every
/// allocation/plan-construction boundary and enables immediate single-owner runtime
/// mutation. This is the minimal kernel world, not the later scheduler/host facade.
/// </remarks>
internal sealed class EcsWorld<TArena> : IDisposable
    where TArena : class, IChunkArenaBackend
{
    private readonly ComponentSchema _schema;
    private readonly EntityPool _entities;
    private readonly ArchetypeRegistry<TArena> _archetypes;
    private EcsWorldState _state;

    internal EcsWorld(
        ComponentSchema schema,
        EntityCapacity entityCapacity,
        int maximumArchetypes,
        Func<ChunkArenaCapacity, TArena> arenaFactory)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(arenaFactory);

        _schema = schema;
        _entities = new EntityPool(WorldId.Create(), entityCapacity);
        _archetypes = new ArchetypeRegistry<TArena>(schema, maximumArchetypes, arenaFactory);
        _state = EcsWorldState.Created;
    }

    internal WorldId Id => _entities.WorldId;

    internal ComponentSchema Schema => _schema;

    internal EntityCapacity EntityCapacity => _entities.Capacity;

    internal EcsWorldState State => _state;

    internal bool IsSealed => _state == EcsWorldState.Sealed;

    internal bool IsDisposed => _state == EcsWorldState.Disposed;

    internal int AliveCount
    {
        get
        {
            ThrowIfDisposed();
            return _entities.AliveCount;
        }
    }

    internal int LocatedCount
    {
        get
        {
            ThrowIfDisposed();
            return _entities.LocatedCount;
        }
    }

    internal int ArchetypeCount
    {
        get
        {
            ThrowIfDisposed();
            return _archetypes.Count;
        }
    }

    internal ArchetypeId RegisterArchetype(
        ReadOnlySpan<ComponentId> canonicalSignature,
        ChunkLayoutOptions layoutOptions,
        int maximumChunks)
    {
        EnsureCreated();
        return _archetypes.Register(canonicalSignature, layoutOptions, maximumChunks);
    }

    internal ArchetypeTransitionPlan<TArena> PrepareTransition(
        ArchetypeId sourceId,
        ArchetypeId destinationId)
    {
        EnsureCreated();
        return _archetypes.PrepareTransition(sourceId, destinationId);
    }

    internal ArchetypeComponentBinding<T> BindComponent<T>(
        ArchetypeId archetypeId,
        ComponentId generatedComponentId)
        where T : unmanaged, IComponent<T>
    {
        EnsureCreated();
        if (!_schema.TryGetComponentId<T>(out ComponentId actualComponentId) ||
            actualComponentId != generatedComponentId)
        {
            throw new ArgumentException(
                "The generated component ID does not identify the requested type in this world schema.",
                nameof(generatedComponentId));
        }

        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(archetypeId);
        int columnIndex = storage.Store.GetColumnIndex(generatedComponentId);
        if (columnIndex < 0)
        {
            throw new ArgumentException(
                "The archetype does not contain the requested generated component.",
                nameof(archetypeId));
        }

        ref readonly ComponentMetadata metadata = ref _schema.GetMetadata(generatedComponentId);
        ref readonly ComponentColumnLayout column = ref storage.Columns[columnIndex];
        if (metadata.Flags != column.Flags ||
            metadata.Size != column.ElementSizeBytes ||
            (!metadata.IsTag && metadata.Size != Unsafe.SizeOf<T>()))
        {
            ThrowWorldInvariant("Generated component metadata and archetype storage disagree.");
        }

        return new ArchetypeComponentBinding<T>(
            Id,
            archetypeId,
            generatedComponentId,
            columnIndex,
            metadata.Flags);
    }

    internal bool TryGetArchetypeId(
        ReadOnlySpan<ComponentId> canonicalSignature,
        out ArchetypeId archetypeId)
    {
        ThrowIfDisposed();
        return _archetypes.TryGetId(canonicalSignature, out archetypeId);
    }

    internal ArchetypeStorage<TArena> GetArchetypeStorage(ArchetypeId archetypeId)
    {
        ThrowIfDisposed();
        return _archetypes.GetStorage(archetypeId);
    }

    internal void Seal()
    {
        ThrowIfDisposed();
        if (_state == EcsWorldState.Sealed)
            return;

        _archetypes.Seal();
        _state = EcsWorldState.Sealed;
    }

    internal bool IsAlive(Entity entity)
    {
        EnsureSealed();
        return _entities.IsAlive(entity);
    }

    internal bool TryGetLocation(Entity entity, out EntityLocation location)
    {
        EnsureSealed();
        return _entities.TryGetLocation(entity, out location);
    }

    internal bool TryCreateDefault(ArchetypeId archetypeId, out Entity entity)
    {
        EnsureSealed();
        return TryCreateDefaultCore(archetypeId, out entity, out _);
    }

    internal bool TryCreateInitialized<T>(
        ArchetypeId archetypeId,
        in ArchetypeComponentBinding<T> binding,
        in T initialValue,
        out Entity entity)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: true);
        if (binding.ArchetypeId != archetypeId)
        {
            throw new ArgumentException(
                "The initialization binding belongs to a different archetype.",
                nameof(binding));
        }
        if (!TryCreateDefaultCore(archetypeId, out entity, out EntityLocation location))
            return false;

        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(archetypeId);
        storage.Store.GetBoundComponentRef<T>(location.ChunkRow, binding.ColumnIndex) = initialValue;
        return true;
    }

    internal bool HasComponent<T>(Entity entity, in ArchetypeComponentBinding<T> binding)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: false);
        return _entities.TryGetLocation(entity, out EntityLocation location) &&
            location.ArchetypeId == binding.ArchetypeId;
    }

    internal bool TryGetComponent<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        out T value)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: true);
        if (!TryResolveBinding(entity, binding, out ArchetypeStorage<TArena>? storage, out ChunkRow row))
        {
            value = default;
            return false;
        }

        value = storage.Store.GetBoundComponentRef<T>(row, binding.ColumnIndex);
        return true;
    }

    internal bool TrySetComponent<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        in T value)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: true);
        if (!TryResolveBinding(entity, binding, out ArchetypeStorage<TArena>? storage, out ChunkRow row))
            return false;

        storage.Store.GetBoundComponentRef<T>(row, binding.ColumnIndex) = value;
        return true;
    }

    internal bool TryGetComponentEnabled<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        out bool enabled)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateEnableBinding(binding);
        if (!TryResolveBinding(entity, binding, out ArchetypeStorage<TArena>? storage, out ChunkRow row))
        {
            enabled = false;
            return false;
        }

        enabled = storage.Store.IsBoundEnabled(row, binding.ColumnIndex);
        return true;
    }

    internal bool TrySetComponentEnabled<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        bool enabled)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateEnableBinding(binding);
        if (!TryResolveBinding(entity, binding, out ArchetypeStorage<TArena>? storage, out ChunkRow row))
            return false;

        storage.Store.SetBoundEnabled(row, binding.ColumnIndex, enabled);
        return true;
    }

    private bool TryCreateDefaultCore(
        ArchetypeId archetypeId,
        out Entity entity,
        out EntityLocation location)
    {
        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(archetypeId);

        // Avoid consuming/recycling an entity generation when the target store is
        // already known to be full. Structural mutation is single-owner here.
        if ((long)storage.Store.Count >= storage.Store.MaximumRows ||
            !_entities.TryAllocate(out entity))
        {
            entity = Entity.Null;
            location = EntityLocation.Invalid;
            return false;
        }

        if (!storage.Store.TryAdd(entity.Index, out ChunkRow row))
        {
            if (!_entities.TryFree(entity))
                ThrowWorldInvariant("An unlocated entity could not be returned after row-capacity rejection.");
            entity = Entity.Null;
            location = EntityLocation.Invalid;
            return false;
        }

        location = new EntityLocation(archetypeId, row);
        if (_entities.TryBindLocation(entity, location))
            return true;

        RollBackJustAddedRow(storage.Store, row, entity.Index);
        if (!_entities.TryFree(entity))
            ThrowWorldInvariant("An entity could not be returned after location publication rejection.");
        entity = Entity.Null;
        location = EntityLocation.Invalid;
        ThrowWorldInvariant("A newly allocated entity rejected its first location publication.");
        return false;
    }

    internal bool TryTransition(
        Entity entity,
        ArchetypeTransitionPlan<TArena> plan,
        out EntityLocation destinationLocation)
    {
        EnsureSealed();
        ArgumentNullException.ThrowIfNull(plan);
        if (!_archetypes.Owns(plan))
        {
            throw new ArgumentException(
                "The archetype transition plan belongs to a different world registry.",
                nameof(plan));
        }

        return ArchetypeMigrator.TryMigrate(
            _entities,
            entity,
            plan,
            out destinationLocation);
    }

    internal bool TryDestroy(Entity entity)
    {
        EnsureSealed();
        if (!_entities.TryGetLocation(entity, out EntityLocation location))
            return false;
        if (!_archetypes.TryGetStorage(location.ArchetypeId, out ArchetypeStorage<TArena>? storage))
            ThrowWorldInvariant("A live entity location refers to an unknown archetype.");
        ArchetypeStorage<TArena> ownedStorage = storage!;
        if (!ownedStorage.Store.Contains(location.ChunkRow, entity.Index))
            ThrowWorldInvariant("A live entity location does not match its archetype row.");
        if (!ownedStorage.Store.TryRemove(
                location.ChunkRow,
                entity.Index,
                out ChunkRowRelocation relocation))
        {
            ThrowWorldInvariant("A validated archetype row rejected entity destruction.");
        }

        _entities.ApplyRelocation(ownedStorage.Id, relocation);
        if (!_entities.TryUnbindLocation(entity, location))
            ThrowWorldInvariant("A destroyed entity rejected its expected location removal.");
        if (!_entities.TryFree(entity))
            ThrowWorldInvariant("An unlocated destroyed entity could not return to the entity pool.");
        return true;
    }

    public void Dispose()
    {
        if (_state == EcsWorldState.Disposed)
            return;

        try
        {
            _archetypes.Dispose();
        }
        finally
        {
            _state = EcsWorldState.Disposed;
        }
    }

    private bool TryResolveBinding<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        [NotNullWhen(true)] out ArchetypeStorage<TArena>? storage,
        out ChunkRow row)
        where T : unmanaged, IComponent<T>
    {
        if (!_entities.TryGetLocation(entity, out EntityLocation location) ||
            location.ArchetypeId != binding.ArchetypeId)
        {
            storage = null;
            row = ChunkRow.Invalid;
            return false;
        }

        storage = _archetypes.GetStorage(binding.ArchetypeId);
        row = location.ChunkRow;
        return true;
    }

    private void ValidateBinding<T>(
        in ArchetypeComponentBinding<T> binding,
        bool requireData)
        where T : unmanaged, IComponent<T>
    {
        if (!binding.IsValid)
            throw new ArgumentException("A valid archetype component binding is required.", nameof(binding));
        if (binding.WorldId != Id)
        {
            throw new ArgumentException(
                "The archetype component binding belongs to a different world.",
                nameof(binding));
        }
        if (requireData && !binding.HasData)
            throw new InvalidOperationException("Tag components do not contain readable or writable data.");
    }

    private void ValidateEnableBinding<T>(in ArchetypeComponentBinding<T> binding)
        where T : unmanaged, IComponent<T>
    {
        ValidateBinding(binding, requireData: false);
        if (!binding.IsEnableable)
            throw new InvalidOperationException("The bound component is not enableable.");
    }

    private static void RollBackJustAddedRow(
        ArchetypeChunkStore<TArena> store,
        ChunkRow row,
        uint entityIndex)
    {
        if (!store.TryRemove(row, entityIndex, out ChunkRowRelocation relocation) ||
            relocation.HasValue)
        {
            ThrowWorldInvariant("A just-added archetype row could not be rolled back in place.");
        }
    }

    private void EnsureCreated()
    {
        ThrowIfDisposed();
        if (_state != EcsWorldState.Created)
            throw new InvalidOperationException("World registration is closed after sealing.");
    }

    private void EnsureSealed()
    {
        ThrowIfDisposed();
        if (_state != EcsWorldState.Sealed)
            throw new InvalidOperationException("World runtime mutation requires a sealed world.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowWorldInvariant(string message) =>
        throw new InvalidOperationException(message);
}
