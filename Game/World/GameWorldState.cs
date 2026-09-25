using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Post-step maintenance diagnostics for one generation-owned world index.</summary>
public readonly record struct GameWorldStateSnapshot(
    bool IsInitialized,
    long CommitCount,
    long FullRebuildCount,
    long LifecycleFactsObserved,
    long ChangedRowsObserved,
    long ReconciledEntities,
    WorldOverlayRevision OverlayRevision,
    int ChangedOverlayChunks,
    WorldSpatialIndexSnapshot Index,
    ChunkPublicationStoreSnapshot Publication,
    long ReservedBytes);

/// <summary>
/// Game-owned derived world state paired with one authoritative ECS generation.
/// </summary>
public sealed class GameWorldState : IDisposable
{
    public static readonly SpatialSpaceId DefaultSpace = new(1);

    private readonly WorldId _worldId;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly int _entityCapacity;
    private readonly ComponentLookup<WorldPositionComponent> _positionLookup;
    private readonly ComponentLookup<WorldPublicComponent> _publicLookup;
    private readonly ReadJobPlan<WorldPositionComponent> _baseline;
    private readonly ChangedRowsJobPlan<WorldPositionComponent, WorldPositionComponent> _positionChanges;
    private readonly ChangedRowsJobPlan<WorldPublicComponent, WorldPublicComponent> _publicChanges;
    private readonly ComponentId _positionComponentId;
    private readonly ComponentId _publicComponentId;
    private readonly LifecycleRecord[] _lifecycleScratch;
    private readonly uint[] _entityScratch;
    private readonly Entity[] _stagedEntities;
    private readonly uint[] _stageStamps;
    private readonly WorldPublicChange[] _publicationChanges;
    private ChangeCursor _positionChangeCursor;
    private ChangeCursor _publicChangeCursor;
    private LifecycleCursor _lifecycleCursor;
    private uint _stageStamp;
    private bool _initialized;
    private bool _disposed;
    private long _commitCount;
    private long _fullRebuildCount;
    private long _lifecycleFactsObserved;
    private long _changedRowsObserved;
    private long _reconciledEntities;

    public GameWorldState(
        EcsWorld world,
        BoundWorldPack? baseWorld = null,
        ChunkPublicationLimits? publicationLimits = null,
        WorldMutableOverlay? overlay = null,
        PreparedWorldComposition? preparedComposition = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (baseWorld is not null && baseWorld.Space != DefaultSpace)
        {
            throw new ArgumentException(
                $"The base WorldPack belongs to {baseWorld.Space}; this game generation requires {DefaultSpace}.",
                nameof(baseWorld));
        }
        if (baseWorld is null && overlay is not null)
            throw new ArgumentException("A mutable overlay requires an exact immutable base world.", nameof(overlay));
        if (preparedComposition is not null && (baseWorld is not null || overlay is not null))
            throw new ArgumentException("Legacy and canonical structural roots are mutually exclusive.", nameof(preparedComposition));
        if (preparedComposition is not null && preparedComposition.Space != DefaultSpace)
            throw new ArgumentException("The prepared composition belongs to a different Game space.", nameof(preparedComposition));
        if (preparedComposition is not null && !preparedComposition.TryValidatePreparedSubset(out _))
            throw new ArgumentException("The prepared composition has conflicting Packs.", nameof(preparedComposition));
        if (world.Options.LifecycleRecordCapacity <= 0)
            throw new ArgumentException("Spatial-enabled worlds require retained ECS lifecycle history.", nameof(world));
        _worldId = world.Id;
        _entityCapacity = world.Options.EntityCapacity.MaximumEntities;
        ChunkPublicationLimits limits = publicationLimits ?? ChunkPublicationLimits.ForEntityCapacity(_entityCapacity);
        if (limits.MaximumPublicEntities < _entityCapacity)
            throw new ArgumentException(
                "Publication capacity must address every admitted ECS entity slot.",
                nameof(publicationLimits));
        _positionLookup = world.PrepareLookup<WorldPositionComponent>();
        _publicLookup = world.PrepareLookup<WorldPublicComponent>();
        _baseline = world.PrepareReadJob<WorldPositionComponent>();
        _positionChanges = world.PrepareChangedRowsJob<WorldPositionComponent, WorldPositionComponent>();
        _publicChanges = world.PrepareChangedRowsJob<WorldPublicComponent, WorldPublicComponent>();
        if (!world.Schema.TryGetComponentId<WorldPositionComponent>(out _positionComponentId))
            throw new ArgumentException("World schema does not contain WorldPositionComponent.", nameof(world));
        if (!world.Schema.TryGetComponentId<WorldPublicComponent>(out _publicComponentId))
            throw new ArgumentException("World schema does not contain WorldPublicComponent.", nameof(world));
        _lifecycleScratch = new LifecycleRecord[world.Options.LifecycleRecordCapacity];
        _entityScratch = new uint[_entityCapacity];
        _stagedEntities = new Entity[_entityCapacity];
        _stageStamps = new uint[_entityCapacity];
        _publicationChanges = new WorldPublicChange[_entityCapacity];
        Index = new WorldSpatialIndex(_entityCapacity);
        Publications = new ChunkPublicationStore(PublicationEpoch.Create(), limits);
        BaseWorld = baseWorld;
        PreparedWorld = baseWorld is null ? null : new WorldPreparedView(baseWorld, overlay);
        PreparedComposition = preparedComposition;
    }

    public WorldId WorldId => _worldId;
    public WorldSpatialIndex Index { get; private set; }
    public ChunkPublicationStore Publications { get; private set; }
    /// <summary>Temporary one-Pack compatibility root, including the current Concept Demo.</summary>
    public BoundWorldPack? BaseWorld { get; private set; }
    /// <summary>Legacy one-Pack view (including Cabin); never live with PreparedComposition.</summary>
    public WorldPreparedView? PreparedWorld { get; private set; }
    /// <summary>Canonical immutable World structural root. Not yet supported by worldview/v1 saves.</summary>
    public PreparedWorldComposition? PreparedComposition { get; private set; }
    public WorldMutableOverlay? Overlay => PreparedWorld?.Overlay;

    /// <summary>Installs the fully resolved current-world view into an unpublished staging shell.</summary>
    public bool TryInstallPreparedWorld(BoundWorldPack? baseWorld, WorldMutableOverlay? overlay)
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized || BaseWorld is not null || PreparedWorld is not null || PreparedComposition is not null ||
            baseWorld is null && overlay is not null ||
            baseWorld is not null && baseWorld.Space != DefaultSpace ||
            overlay is not null &&
                (overlay.BaseWorldFingerprint != baseWorld!.Fingerprint || overlay.Space != baseWorld.Space))
            return false;
        BaseWorld = baseWorld;
        PreparedWorld = baseWorld is null ? null : new WorldPreparedView(baseWorld, overlay);
        return true;
    }

    /// <summary>Installs one validated immutable composition in an unpublished staging shell.</summary>
    public bool TryInstallPreparedComposition(PreparedWorldComposition candidate)
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(candidate);
        if (_initialized || BaseWorld is not null || PreparedWorld is not null ||
            PreparedComposition is not null || candidate.Space != DefaultSpace ||
            !candidate.TryValidatePreparedSubset(out _))
            return false;
        PreparedComposition = candidate;
        return true;
    }

    /// <summary>Atomically publishes one fully validated durable replacement overlay generation.</summary>
    public bool TryPublishOverlay(WorldMutableOverlay candidate)
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(candidate);
        if (BaseWorld is null || candidate.BaseWorldFingerprint != BaseWorld.Fingerprint ||
            candidate.Space != BaseWorld.Space)
            return false;
        ulong current = Overlay?.Revision.Value ?? 0;
        if (!candidate.Revision.IsValid || candidate.Revision.Value != checked(current + 1))
            return false;
        PreparedWorld = new WorldPreparedView(BaseWorld, candidate);
        return true;
    }

    /// <summary>
    /// Samples authoritative current base terrain through the prepared base-plus-overlay view.
    /// </summary>
    public bool TrySampleBaseTerrainHeight(in WorldPosition location, out int heightMillimeters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PreparedComposition is not null)
            return PreparedComposition.TrySampleBaseTerrainHeight(location, out heightMillimeters);
        if (PreparedWorld is null)
        {
            heightMillimeters = 0;
            return false;
        }
        return PreparedWorld.TrySampleBaseTerrainHeight(location, out heightMillimeters);
    }

    /// <summary>
    /// Probes authoritative current base-surface topology. Intentional resident void is distinct
    /// from absent/unprepared world data even though the current grounded locomotion policy rejects
    /// both as non-resolvable endpoints.
    /// </summary>
    public BaseTerrainSurfaceSample ProbeBaseTerrainSurface(in WorldPosition location)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PreparedComposition is not null)
            return PreparedComposition.ProbeBaseTerrainSurface(location);
        return PreparedWorld?.ProbeBaseTerrainSurface(location) ?? BaseTerrainSurfaceSample.Unavailable;
    }

    /// <summary>Detailed canonical authorship/residency; unavailable for legacy one-Pack worlds.</summary>
    public bool TryProbeStructuralChunk(SpatialChunkKey key, out WorldStructuralChunkProbe probe)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PreparedComposition is not null)
            return PreparedComposition.TryProbeStructuralChunk(key, out probe);
        probe = default;
        return false;
    }

    /// <summary>Detailed canonical base-cell state; unavailable for legacy one-Pack worlds.</summary>
    public bool TryProbeBaseTerrainResolution(
        in WorldPosition location, out WorldBaseTerrainResolution resolution)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PreparedComposition is not null)
            return PreparedComposition.TryProbeBaseTerrainResolution(location, out resolution);
        resolution = default;
        return false;
    }

    /// <summary>
    /// Resolves authoritative current base-terrain material through the prepared base-plus-overlay view.
    /// </summary>
    public bool TryResolveBaseTerrainMaterial(in WorldPosition location, out ushort materialId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PreparedComposition is not null)
            return PreparedComposition.TryResolveBaseTerrainMaterial(location, out materialId);
        if (PreparedWorld is null)
        {
            materialId = 0;
            return false;
        }
        return PreparedWorld.TryResolveBaseTerrainMaterial(location, out materialId);
    }

    /// <summary>Reads the complete authoritative vertical terrain column from one structural root.</summary>
    public WorldTerrainColumnQueryStatus TryCopyTerrainColumnSurfaces(
        in WorldPosition location, Span<WorldTerrainColumnSurface> destination,
        out int count, out int required)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PreparedComposition is not null)
            return PreparedComposition.TryCopyTerrainColumnSurfaces(location, destination, out count, out required);
        if (PreparedWorld is not null)
            return PreparedWorld.TryCopyTerrainColumnSurfaces(location, destination, out count, out required);
        count = required = 0;
        return WorldTerrainColumnQueryStatus.Unavailable;
    }

    public WorldTerrainColumnQueryStatus TryFindHighestSupportAtOrBelow(
        in WorldPosition location, int maximumHeightMillimeters, out WorldTerrainColumnSurface support)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PreparedComposition is not null)
            return PreparedComposition.TryFindHighestSupportAtOrBelow(location, maximumHeightMillimeters, out support);
        if (PreparedWorld is not null)
            return PreparedWorld.TryFindHighestSupportAtOrBelow(location, maximumHeightMillimeters, out support);
        support = default;
        return WorldTerrainColumnQueryStatus.Unavailable;
    }

    public WorldTerrainColumnQueryStatus TryFindNearestSupport(
        in WorldPosition location, int requestedHeightMillimeters, out WorldTerrainColumnSurface support)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PreparedComposition is not null)
            return PreparedComposition.TryFindNearestSupport(location, requestedHeightMillimeters, out support);
        if (PreparedWorld is not null)
            return PreparedWorld.TryFindNearestSupport(location, requestedHeightMillimeters, out support);
        support = default;
        return WorldTerrainColumnQueryStatus.Unavailable;
    }

    public WorldTerrainColumnQueryStatus TryFindLowestCeilingAtOrAbove(
        in WorldPosition location, int minimumHeightMillimeters, out WorldTerrainColumnSurface ceiling)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PreparedComposition is not null)
            return PreparedComposition.TryFindLowestCeilingAtOrAbove(location, minimumHeightMillimeters, out ceiling);
        if (PreparedWorld is not null)
            return PreparedWorld.TryFindLowestCeilingAtOrAbove(location, minimumHeightMillimeters, out ceiling);
        ceiling = default;
        return WorldTerrainColumnQueryStatus.Unavailable;
    }

    /// <summary>
    /// Queries authoritative current-world static geometry through the prepared base-plus-overlay view.
    /// </summary>
    public bool TryQueryStaticBounds(
        in WorldStaticBounds bounds,
        int maximumCandidateChunks,
        Span<WorldStaticQueryHit> destination,
        out int count,
        out int required,
        out WorldStaticQueryError error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateChunks);
        count = 0;
        required = 0;
        if (!bounds.IsValid)
        {
            error = WorldStaticQueryError.InvalidBounds;
            return false;
        }
        if (PreparedComposition is not null)
            return PreparedComposition.TryQueryStaticBounds(bounds, maximumCandidateChunks, destination,
                out count, out required, out error);
        if (PreparedWorld is null)
        {
            error = WorldStaticQueryError.None;
            return true;
        }
        return PreparedWorld.TryQueryStaticBounds(bounds, maximumCandidateChunks, destination,
            out count, out required, out error);
    }

    public bool TrySetPosition(EcsWorld world, Entity entity, in WorldPosition position)
    {
        EnsureWorld(world);
        return world.TryWrite(entity, _positionLookup, new WorldPositionComponent(position));
    }

    public bool TryReadPosition(EcsWorld world, Entity entity, out WorldPosition position)
    {
        EnsureWorld(world);
        if (world.TryRead(entity, _positionLookup, out WorldPositionComponent component) &&
            component.TryGetValue(out position))
            return true;
        position = default;
        return false;
    }

    public bool TrySetPublic(EcsWorld world, Entity entity, in WorldPublicDescriptor descriptor)
    {
        EnsureWorld(world);
        return world.TryWrite(entity, _publicLookup, new WorldPublicComponent(descriptor));
    }

    public bool TryReadPublic(EcsWorld world, Entity entity, out WorldPublicDescriptor descriptor)
    {
        EnsureWorld(world);
        if (world.TryRead(entity, _publicLookup, out WorldPublicComponent component) &&
            component.TryGetValue(out descriptor))
            return true;
        descriptor = default;
        return false;
    }

    public void Initialize(EcsWorld world)
    {
        EnsureWorld(world);
        if (_initialized)
            return;
        Rebuild(world);
    }

    /// <summary>Atomically replaces derived membership from current ECS truth.</summary>
    public void Rebuild(EcsWorld world)
    {
        EnsureWorld(world);
        RebuildCore(world);
    }

    private void RebuildCore(EcsWorld world)
    {
        var candidate = new WorldSpatialIndex(_entityCapacity);
        var candidatePublications = new ChunkPublicationStore(
            _initialized ? PublicationEpoch.Create() : Publications.Epoch,
            Publications.Limits);
        var visitor = new BaselineVisitor(_entityScratch, _stageStamps, NextStageStamp(), _entityCapacity);
        _ = world.ExecuteJob(_baseline, ref visitor);
        Array.Sort(_entityScratch, 0, visitor.Count);
        for (int index = 0; index < visitor.Count; index++)
        {
            uint entityIndex = visitor.Indices[index];
            ReconcileCurrent(world, candidate, entityIndex);
            _publicationChanges[index] = ReadPublicationChange(world, entityIndex, default);
        }
        if (!candidatePublications.TryCommit(
                _publicationChanges.AsSpan(0, visitor.Count),
                out ChunkPublicationCommitError publicationError))
            throw new InvalidOperationException($"Public-world rebuild failed: {publicationError}.");

        LifecycleCursor lifecycle = world.CaptureLifecycleCursorFromNow();
        ChangeCursor positionChanges = world.CreateChangeCursorFromNow();
        ChangeCursor publicChanges = world.CreateChangeCursorFromNow();
        Index = candidate;
        Publications = candidatePublications;
        _lifecycleCursor = lifecycle;
        _positionChangeCursor = positionChanges;
        _publicChangeCursor = publicChanges;
        _initialized = true;
        _fullRebuildCount++;
        _reconciledEntities += visitor.Count;
    }

    /// <summary>Consumes one final post-ECS-step change set and commits membership.</summary>
    public void CommitAfterEcsStep(EcsWorld world)
    {
        EnsureWorld(world);
        if (!_initialized)
            throw new InvalidOperationException("World spatial state was not initialized before simulation.");

        LifecycleCursor stagedLifecycle = _lifecycleCursor;
        LifecycleReadResult lifecycle = world.ReadLifecycle(ref stagedLifecycle, _lifecycleScratch);
        if (lifecycle.Status == LifecycleReadStatus.ResyncRequired || lifecycle.RemainingCount != 0)
        {
            long recoveryCommit = checked(_commitCount + 1);
            RebuildCore(world);
            _commitCount = recoveryCommit;
            return;
        }

        uint stamp = NextStageStamp();
        int count = 0;
        for (int index = 0; index < lifecycle.Count; index++)
        {
            ref readonly LifecycleRecord record = ref _lifecycleScratch[index];
            if (record.Kind is LifecycleKind.Created or LifecycleKind.Destroyed ||
                record.ComponentId == _positionComponentId || record.ComponentId == _publicComponentId)
                Stage(record.Entity.Index, record.Entity, stamp, ref count);
        }

        ChangeCursor stagedPositionChanges = _positionChangeCursor;
        var changedPositions = new PositionChangedVisitor(_entityScratch, _stageStamps, stamp, count);
        int changedRows = world.ExecuteChangedRowsJob(
            _positionChanges, ref stagedPositionChanges, ref changedPositions);
        count = changedPositions.Count;
        ChangeCursor stagedPublicChanges = _publicChangeCursor;
        var changedPublic = new PublicChangedVisitor(_entityScratch, _stageStamps, stamp, count);
        changedRows += world.ExecuteChangedRowsJob(
            _publicChanges, ref stagedPublicChanges, ref changedPublic);
        count = changedPublic.Count;
        Array.Sort(_entityScratch, 0, count);

        for (int index = 0; index < count; index++)
        {
            uint entityIndex = _entityScratch[index];
            _publicationChanges[index] = ReadPublicationChange(
                world, entityIndex, _stagedEntities[entityIndex]);
        }
        long nextCommit = checked(_commitCount + 1);
        if (!Publications.TryCommit(
                _publicationChanges.AsSpan(0, count),
                out ChunkPublicationCommitError publicationError))
            throw new InvalidOperationException($"Public-world commit failed: {publicationError}.");
        for (int index = 0; index < count; index++)
            ReconcileCurrent(world, Index, _entityScratch[index]);

        _lifecycleCursor = stagedLifecycle;
        _positionChangeCursor = stagedPositionChanges;
        _publicChangeCursor = stagedPublicChanges;
        _commitCount = nextCommit;
        _lifecycleFactsObserved += lifecycle.Count;
        _changedRowsObserved += changedRows;
        _reconciledEntities += count;
    }

    public GameWorldStateSnapshot GetSnapshot()
    {
        long scratch = checked(
            (long)_lifecycleScratch.Length * Unsafe.SizeOf<LifecycleRecord>() +
            (long)_entityScratch.Length * sizeof(uint) +
            (long)_stagedEntities.Length * Unsafe.SizeOf<Entity>() +
            (long)_publicationChanges.Length * Unsafe.SizeOf<WorldPublicChange>() +
            (long)_stageStamps.Length * sizeof(uint));
        WorldSpatialIndexSnapshot index = Index.GetSnapshot();
        ChunkPublicationStoreSnapshot publication = Publications.GetSnapshot();
        return new GameWorldStateSnapshot(
            _initialized,
            _commitCount,
            _fullRebuildCount,
            _lifecycleFactsObserved,
            _changedRowsObserved,
            _reconciledEntities,
            Overlay?.Revision ?? default,
            Overlay?.ChangedChunkCount ?? 0,
            index,
            publication,
            checked(scratch + index.ReservedBytes + publication.ReservedBytes +
                (Overlay?.ReservedBytes ?? 0)));
    }

    public void Dispose() => _disposed = true;

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Game world state belongs to a different owner thread.");
    }

    private void ReconcileCurrent(EcsWorld world, WorldSpatialIndex index, uint entityIndex)
    {
        if (world.TryGetEntity(entityIndex, out Entity entity) &&
            world.TryRead(entity, _positionLookup, out WorldPositionComponent component))
        {
            if (!component.TryGetValue(out WorldPosition position))
                throw new InvalidOperationException("Authoritative WorldPositionComponent is not canonically normalized.");
            index.Reconcile(entity, position.ToChunkKey(DefaultSpace));
        }
        else
        {
            index.Remove(entityIndex);
        }
    }

    private WorldPublicChange ReadPublicationChange(EcsWorld world, uint entityIndex, Entity stagedEntity)
    {
        if (world.TryGetEntity(entityIndex, out Entity entity))
        {
            if (world.TryRead(entity, _positionLookup, out WorldPositionComponent positionComponent))
            {
                if (!positionComponent.TryGetValue(out WorldPosition position))
                    throw new InvalidOperationException(
                        "Authoritative WorldPositionComponent is not canonically normalized.");
                if (world.TryRead(entity, _publicLookup, out WorldPublicComponent publicComponent))
                {
                    if (!publicComponent.TryGetValue(out WorldPublicDescriptor descriptor))
                        throw new InvalidOperationException(
                            "Authoritative WorldPublicComponent is not canonical.");
                    if ((descriptor.Flags & WorldPublicStateFlags.Visible) == 0)
                        return WorldPublicChange.Absent(entity);
                    return WorldPublicChange.Present(entity, position, descriptor);
                }
            }
            return WorldPublicChange.Absent(entity);
        }
        if (!stagedEntity.IsValid)
            throw new InvalidOperationException("A removed public-world entity has no retained lifecycle identity.");
        return WorldPublicChange.Absent(stagedEntity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Stage(uint entityIndex, Entity entity, uint stamp, ref int count)
    {
        if (entityIndex >= (uint)_stageStamps.Length)
            throw new InvalidOperationException("Spatial change referenced an entity outside configured capacity.");
        _stagedEntities[entityIndex] = entity;
        if (_stageStamps[entityIndex] == stamp) return;
        _stageStamps[entityIndex] = stamp;
        _entityScratch[count++] = entityIndex;
    }

    private uint NextStageStamp()
    {
        if (_stageStamp == uint.MaxValue)
        {
            Array.Clear(_stageStamps);
            _stageStamp = 1;
        }
        else
        {
            _stageStamp++;
        }
        return _stageStamp;
    }

    private void EnsureWorld(EcsWorld world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(world);
        if (world.Id != _worldId)
            throw new ArgumentException("World state belongs to a different ECS world.", nameof(world));
    }

    private struct BaselineVisitor : IReadChunkJob<WorldPositionComponent>
    {
        private readonly uint[] _indices;
        private readonly uint[] _stamps;
        private readonly uint _stamp;
        private readonly int _capacity;

        internal BaselineVisitor(uint[] indices, uint[] stamps, uint stamp, int capacity)
        {
            _indices = indices;
            _stamps = stamps;
            _stamp = stamp;
            _capacity = capacity;
            Count = 0;
        }

        internal int Count;
        internal readonly ReadOnlySpan<uint> Indices => _indices.AsSpan(0, Count);

        public void Execute(in ReadComponentChunk<WorldPositionComponent> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                uint index = chunk.EntityIndices[row];
                if (index >= (uint)_capacity)
                    throw new InvalidOperationException("Spatial baseline exceeded configured entity capacity.");
                if (_stamps[index] == _stamp) continue;
                _stamps[index] = _stamp;
                _indices[Count++] = index;
            }
        }
    }

    private struct PositionChangedVisitor : IReadOnlyChunkJob<WorldPositionComponent>
    {
        private readonly uint[] _indices;
        private readonly uint[] _stamps;
        private readonly uint _stamp;

        internal PositionChangedVisitor(uint[] indices, uint[] stamps, uint stamp, int count)
        {
            _indices = indices;
            _stamps = stamps;
            _stamp = stamp;
            Count = count;
        }

        internal int Count;

        public void Execute(in ReadOnlyComponentChunk<WorldPositionComponent> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                uint index = chunk.EntityIndices[row];
                if (_stamps[index] == _stamp) continue;
                _stamps[index] = _stamp;
                _indices[Count++] = index;
            }
        }
    }

    private struct PublicChangedVisitor : IReadOnlyChunkJob<WorldPublicComponent>
    {
        private readonly uint[] _indices;
        private readonly uint[] _stamps;
        private readonly uint _stamp;

        internal PublicChangedVisitor(uint[] indices, uint[] stamps, uint stamp, int count)
        {
            _indices = indices;
            _stamps = stamps;
            _stamp = stamp;
            Count = count;
        }

        internal int Count;

        public void Execute(in ReadOnlyComponentChunk<WorldPublicComponent> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                uint index = chunk.EntityIndices[row];
                if (_stamps[index] == _stamp) continue;
                _stamps[index] = _stamp;
                _indices[Count++] = index;
            }
        }
    }
}
