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
    WorldSpatialIndexSnapshot Index,
    long ReservedBytes);

/// <summary>
/// Game-owned derived world state paired with one authoritative ECS generation.
/// </summary>
public sealed class GameWorldState : IDisposable
{
    public static readonly SpatialSpaceId DefaultSpace = new(1);

    private readonly WorldId _worldId;
    private readonly int _entityCapacity;
    private readonly ComponentLookup<WorldPositionComponent> _positionLookup;
    private readonly ReadJobPlan<WorldPositionComponent> _baseline;
    private readonly ChangedRowsJobPlan<WorldPositionComponent, WorldPositionComponent> _changes;
    private readonly ComponentId _positionComponentId;
    private readonly LifecycleRecord[] _lifecycleScratch;
    private readonly uint[] _entityScratch;
    private readonly uint[] _stageStamps;
    private ChangeCursor _changeCursor;
    private LifecycleCursor _lifecycleCursor;
    private uint _stageStamp;
    private bool _initialized;
    private bool _disposed;
    private long _commitCount;
    private long _fullRebuildCount;
    private long _lifecycleFactsObserved;
    private long _changedRowsObserved;
    private long _reconciledEntities;

    public GameWorldState(EcsWorld world, BoundWorldPack? baseWorld = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (baseWorld is not null && baseWorld.Space != DefaultSpace)
        {
            throw new ArgumentException(
                $"The base WorldPack belongs to {baseWorld.Space}; this game generation requires {DefaultSpace}.",
                nameof(baseWorld));
        }
        if (world.Options.LifecycleRecordCapacity <= 0)
            throw new ArgumentException("Spatial-enabled worlds require retained ECS lifecycle history.", nameof(world));
        _worldId = world.Id;
        _entityCapacity = world.Options.EntityCapacity.MaximumEntities;
        _positionLookup = world.PrepareLookup<WorldPositionComponent>();
        _baseline = world.PrepareReadJob<WorldPositionComponent>();
        _changes = world.PrepareChangedRowsJob<WorldPositionComponent, WorldPositionComponent>();
        if (!world.Schema.TryGetComponentId<WorldPositionComponent>(out _positionComponentId))
            throw new ArgumentException("World schema does not contain WorldPositionComponent.", nameof(world));
        _lifecycleScratch = new LifecycleRecord[world.Options.LifecycleRecordCapacity];
        _entityScratch = new uint[_entityCapacity];
        _stageStamps = new uint[_entityCapacity];
        Index = new WorldSpatialIndex(_entityCapacity);
        BaseWorld = baseWorld;
    }

    public WorldId WorldId => _worldId;
    public WorldSpatialIndex Index { get; private set; }
    public BoundWorldPack? BaseWorld { get; }

    /// <summary>
    /// Samples the authoritative current base terrain. T0.4 resolves the immutable base pack directly;
    /// later durable replacements may shadow it behind this game-world route.
    /// </summary>
    public bool TrySampleBaseTerrainHeight(in WorldPosition location, out int heightMillimeters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (BaseWorld is null)
        {
            heightMillimeters = 0;
            return false;
        }
        return BaseWorld.TrySampleBaseTerrainHeight(location, out heightMillimeters);
    }

    /// <summary>
    /// Resolves the authoritative current base-terrain material. T0.4 delegates to the immutable base pack.
    /// </summary>
    public bool TryResolveBaseTerrainMaterial(in WorldPosition location, out ushort materialId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (BaseWorld is null)
        {
            materialId = 0;
            return false;
        }
        return BaseWorld.TryResolveBaseTerrainMaterial(location, out materialId);
    }

    /// <summary>
    /// Queries authoritative immutable static geometry. Later durable overlays may shadow or augment
    /// the base pack behind this game-world route without changing gameplay callers.
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
        if (BaseWorld is null)
        {
            count = 0;
            required = 0;
            error = WorldStaticQueryError.None;
            return true;
        }
        return BaseWorld.TryQueryStaticBounds(bounds, maximumCandidateChunks, destination,
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
        var candidate = new WorldSpatialIndex(_entityCapacity);
        var visitor = new BaselineVisitor(_entityScratch, _stageStamps, NextStageStamp(), _entityCapacity);
        _ = world.ExecuteJob(_baseline, ref visitor);
        for (int index = 0; index < visitor.Count; index++)
            ReconcileCurrent(world, candidate, visitor.Indices[index]);

        LifecycleCursor lifecycle = world.CaptureLifecycleCursorFromNow();
        ChangeCursor changes = world.CreateChangeCursorFromNow();
        Index = candidate;
        _lifecycleCursor = lifecycle;
        _changeCursor = changes;
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
            Rebuild(world);
            _commitCount++;
            return;
        }

        uint stamp = NextStageStamp();
        int count = 0;
        for (int index = 0; index < lifecycle.Count; index++)
        {
            ref readonly LifecycleRecord record = ref _lifecycleScratch[index];
            if (record.Kind is LifecycleKind.Created or LifecycleKind.Destroyed ||
                record.ComponentId == _positionComponentId)
                Stage(record.Entity.Index, stamp, ref count);
        }

        ChangeCursor stagedChanges = _changeCursor;
        var changed = new ChangedVisitor(_entityScratch, _stageStamps, stamp, count);
        int changedRows = world.ExecuteChangedRowsJob(_changes, ref stagedChanges, ref changed);
        count = changed.Count;
        for (int index = 0; index < count; index++)
            ReconcileCurrent(world, Index, _entityScratch[index]);

        _lifecycleCursor = stagedLifecycle;
        _changeCursor = stagedChanges;
        _commitCount++;
        _lifecycleFactsObserved += lifecycle.Count;
        _changedRowsObserved += changedRows;
        _reconciledEntities += count;
    }

    public GameWorldStateSnapshot GetSnapshot()
    {
        long scratch = checked(
            (long)_lifecycleScratch.Length * Unsafe.SizeOf<LifecycleRecord>() +
            (long)_entityScratch.Length * sizeof(uint) +
            (long)_stageStamps.Length * sizeof(uint));
        WorldSpatialIndexSnapshot index = Index.GetSnapshot();
        return new GameWorldStateSnapshot(
            _initialized,
            _commitCount,
            _fullRebuildCount,
            _lifecycleFactsObserved,
            _changedRowsObserved,
            _reconciledEntities,
            index,
            checked(scratch + index.ReservedBytes));
    }

    public void Dispose() => _disposed = true;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Stage(uint entityIndex, uint stamp, ref int count)
    {
        if (entityIndex >= (uint)_stageStamps.Length)
            throw new InvalidOperationException("Spatial change referenced an entity outside configured capacity.");
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

    private struct ChangedVisitor : IReadOnlyChunkJob<WorldPositionComponent>
    {
        private readonly uint[] _indices;
        private readonly uint[] _stamps;
        private readonly uint _stamp;

        internal ChangedVisitor(uint[] indices, uint[] stamps, uint stamp, int count)
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
}
