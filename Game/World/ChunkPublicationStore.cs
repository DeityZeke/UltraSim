using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UltraSim.ECS;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct PublicationEpoch : IEquatable<PublicationEpoch>, IComparable<PublicationEpoch>
{
    private static long s_next;

    public PublicationEpoch(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;

    public static PublicationEpoch Create()
    {
        long value = Interlocked.Increment(ref s_next);
        if (value <= 0)
            throw new InvalidOperationException("The process publication-epoch identity space is exhausted.");
        return new PublicationEpoch(checked((ulong)value));
    }

    public int CompareTo(PublicationEpoch other) => Value.CompareTo(other.Value);
    public bool Equals(PublicationEpoch other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is PublicationEpoch other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"PublicationEpoch({Value})" : "PublicationEpoch(Invalid)";
    public static bool operator ==(PublicationEpoch left, PublicationEpoch right) => left.Equals(right);
    public static bool operator !=(PublicationEpoch left, PublicationEpoch right) => !left.Equals(right);
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct WorldPublicEntityId : IEquatable<WorldPublicEntityId>, IComparable<WorldPublicEntityId>
{
    public WorldPublicEntityId(uint index, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        Index = index;
        Generation = generation;
    }

    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid => Generation != 0;
    public int CompareTo(WorldPublicEntityId other)
    {
        int index = Index.CompareTo(other.Index);
        return index != 0 ? index : Generation.CompareTo(other.Generation);
    }
    public bool Equals(WorldPublicEntityId other) => Index == other.Index && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is WorldPublicEntityId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Index, Generation);
    public override string ToString() => IsValid ? $"PublicEntity({Index}:{Generation})" : "PublicEntity(Invalid)";
    public static bool operator ==(WorldPublicEntityId left, WorldPublicEntityId right) => left.Equals(right);
    public static bool operator !=(WorldPublicEntityId left, WorldPublicEntityId right) => !left.Equals(right);
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct ChunkPublicationRevision : IEquatable<ChunkPublicationRevision>, IComparable<ChunkPublicationRevision>
{
    public ChunkPublicationRevision(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(ChunkPublicationRevision other) => Value.CompareTo(other.Value);
    public bool Equals(ChunkPublicationRevision other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ChunkPublicationRevision other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"ChunkRevision({Value})" : "ChunkRevision(Invalid)";
    public static bool operator ==(ChunkPublicationRevision left, ChunkPublicationRevision right) => left.Equals(right);
    public static bool operator !=(ChunkPublicationRevision left, ChunkPublicationRevision right) => !left.Equals(right);
}

[Flags]
public enum WorldPublicFieldMask : ushort
{
    None = 0,
    Transform = 1 << 0,
    Prefab = 1 << 1,
    Orientation = 1 << 2,
    StateFlags = 1 << 3,
    Complete = Transform | Prefab | Orientation | StateFlags,
}

public enum WorldPublicOperation : byte
{
    Present = 1,
    Remove = 2,
}

public enum ChunkPublicationReadStatus : byte
{
    Snapshot = 1,
    Delta = 2,
    NoChanges = 3,
    SnapshotRequired = 4,
    /// <summary>
    /// The authoritative snapshot is empty because this key has no resident publication metadata.
    /// Consumers clear the retained page and retain no cursor until a later snapshot publishes the key.
    /// </summary>
    ChunkAbsent = 5,
    DestinationTooSmall = 6,
    FutureCursor = 7,
}

public enum ChunkPublicationCommitError : byte
{
    None = 0,
    NonCanonicalChanges,
    IdentityExhausted,
    RevisionExhausted,
    InvalidPublicState,
}

public readonly record struct ChunkPublicationLimits(
    int MaximumPublicEntities,
    int MaximumResidentChunks,
    int RowsPerPage,
    int HistoryEntryCapacity)
{
    public static ChunkPublicationLimits ForEntityCapacity(int entityCapacity) => new(
        entityCapacity,
        checked(entityCapacity * 2),
        8,
        checked(entityCapacity * 4));

    public bool IsValid =>
        MaximumPublicEntities > 0 &&
        MaximumResidentChunks >= (long)MaximumPublicEntities * 2 &&
        RowsPerPage is >= 1 and <= 256 &&
        (RowsPerPage & (RowsPerPage - 1)) == 0 &&
        HistoryEntryCapacity > 0;

    internal void Validate()
    {
        if (!IsValid)
            throw new ArgumentOutOfRangeException(nameof(MaximumPublicEntities),
                "Publication limits require positive entity/history capacities, power-of-two pages, and two chunk slots per entity.");
        _ = checked(MaximumPublicEntities * RowsPerPage);
    }
}

public readonly record struct WorldPublicChange(
    Entity Entity,
    bool IsPublic,
    WorldPosition Position,
    WorldPublicDescriptor Descriptor)
{
    public static WorldPublicChange Absent(Entity entity) => new(entity, false, default, default);
    public static WorldPublicChange Present(
        Entity entity,
        in WorldPosition position,
        in WorldPublicDescriptor descriptor) => new(entity, true, position, descriptor);
}

public readonly record struct WorldPublicRecord(
    WorldPublicEntityId Id,
    ulong EntityPublicVersion,
    uint PrefabId,
    WorldPosition Position,
    WorldYaw Orientation,
    WorldPublicStateFlags StateFlags,
    ChunkPublicationRevision LastChangedRevision);

public readonly record struct WorldPublicDeltaRecord(
    WorldPublicOperation Operation,
    WorldPublicFieldMask Fields,
    WorldPublicEntityId Id,
    ulong EntityPublicVersion,
    WorldPublicRecord Current);

/// <summary>A revision cursor bound to exactly one publication epoch and spatial chunk key.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly record struct ChunkPublicationCursor(
    PublicationEpoch Epoch,
    SpatialChunkKey Key,
    ChunkPublicationRevision Revision)
{
    public bool IsValid => Epoch.IsValid && Key.Space.IsValid && Revision.IsValid;
}

public readonly record struct ChunkPublicationBatch(
    ChunkPublicationReadStatus Status,
    SpatialChunkKey Key,
    ChunkPublicationCursor Cursor,
    ChunkPublicationRevision HistoryFloor,
    int Count,
    int Required);

public readonly record struct ChunkPublicationStoreSnapshot(
    PublicationEpoch Epoch,
    int PublicEntityCount,
    int ResidentChunkCount,
    int PageCount,
    int HistoryEntryCount,
    int HistoryEntryCapacity,
    ulong CommitCount,
    ulong PublishedChunkRevisions,
    ulong HistoryInvalidations,
    int LargestDirtyCommit,
    ulong SnapshotReads,
    ulong DeltaReads,
    ulong SnapshotFallbacks,
    long ReservedBytes,
    ulong PresentChanges,
    ulong RemoveChanges,
    ulong TransformChanges,
    ulong PrefabChanges,
    ulong OrientationChanges,
    ulong StateFlagsChanges,
    ulong CompleteRecords,
    ulong PartialRecords);

/// <summary>
/// Generation-owned packed current public state plus observer-independent bounded dirty history.
/// All mutation and scratch-backed query operations are owner-thread bound.
/// </summary>
public sealed class ChunkPublicationStore
{
    private readonly int _ownerThreadId;
    private readonly ChunkPublicationLimits _limits;
    private readonly EntityMap[] _entityMap;
    private readonly PublicSlot[] _slots;
    private readonly int[] _freeSlots;
    private readonly ChunkRecord[] _chunks;
    private readonly int[] _freeChunks;
    private readonly SpatialChunkKey[] _mapKeys;
    private readonly int[] _mapValues;
    private readonly byte[] _mapStates;
    private readonly int _mapMask;
    private readonly int[] _pageNext;
    private readonly int[] _pagePrevious;
    private readonly int[] _pageCounts;
    private readonly int[] _pageChunks;
    private readonly int[] _freePages;
    private readonly uint[] _rowEntityIndices;
    private readonly uint[] _rowEntityGenerations;
    private readonly ulong[] _rowVersions;
    private readonly uint[] _rowPrefabIds;
    private readonly ushort[] _rowLocalX;
    private readonly ushort[] _rowLocalZ;
    private readonly int[] _rowY;
    private readonly ushort[] _rowOrientations;
    private readonly ushort[] _rowFlags;
    private readonly ulong[] _rowChangedRevisions;
    private readonly HistoryEntry[] _history;
    private readonly int[] _freeHistory;
    private readonly PlannedChange[] _plans;
    private readonly int[] _planAvailableSlots;
    private readonly uint[] _planReuseStamps;
    private readonly SpatialChunkKey[] _affectedKeys;
    private readonly ulong[] _affectedRevisions;
    private readonly PendingDirty[] _pendingDirty;
    private readonly int[] _queryIndices;
    private readonly uint[] _queryStamps;
    private readonly HistoryEntry[] _queryLatest;
    private int _freeSlotCount;
    private int _freeChunkCount;
    private int _freePageCount;
    private int _freeHistoryCount;
    private int _historyGlobalHead = -1;
    private int _historyGlobalTail = -1;
    private int _historyCount;
    private int _entityCount;
    private int _chunkCount;
    private int _pageCount;
    private uint _planStamp;
    private uint _queryStamp;
    private ulong _commitSequence;
    private ulong _revisionSequence;
    private ulong _publishedChunkRevisions;
    private ulong _historyInvalidations;
    private int _largestDirtyCommit;
    private ulong _snapshotReads;
    private ulong _deltaReads;
    private ulong _snapshotFallbacks;
    private ulong _presentChanges;
    private ulong _removeChanges;
    private ulong _transformChanges;
    private ulong _prefabChanges;
    private ulong _orientationChanges;
    private ulong _stateFlagsChanges;
    private ulong _completeRecords;
    private ulong _partialRecords;
    private bool _changeCensusEnabled;
    private int _activeAffectedCount;

    public ChunkPublicationStore(PublicationEpoch epoch, ChunkPublicationLimits limits)
    {
        if (!epoch.IsValid)
            throw new ArgumentException("A publication store requires a valid epoch.", nameof(epoch));
        limits.Validate();
        Epoch = epoch;
        _limits = limits;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        int entities = limits.MaximumPublicEntities;
        int chunks = limits.MaximumResidentChunks;
        int rows = checked(entities * limits.RowsPerPage);
        int mapCapacity = SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo(chunks);
        _entityMap = new EntityMap[entities];
        _slots = new PublicSlot[entities];
        _freeSlots = new int[entities];
        _chunks = new ChunkRecord[chunks];
        _freeChunks = new int[chunks];
        _mapKeys = new SpatialChunkKey[mapCapacity];
        _mapValues = new int[mapCapacity];
        _mapStates = new byte[mapCapacity];
        _mapMask = mapCapacity - 1;
        _pageNext = new int[entities];
        _pagePrevious = new int[entities];
        _pageCounts = new int[entities];
        _pageChunks = new int[entities];
        _freePages = new int[entities];
        _rowEntityIndices = new uint[rows];
        _rowEntityGenerations = new uint[rows];
        _rowVersions = new ulong[rows];
        _rowPrefabIds = new uint[rows];
        _rowLocalX = new ushort[rows];
        _rowLocalZ = new ushort[rows];
        _rowY = new int[rows];
        _rowOrientations = new ushort[rows];
        _rowFlags = new ushort[rows];
        _rowChangedRevisions = new ulong[rows];
        _history = new HistoryEntry[limits.HistoryEntryCapacity];
        _freeHistory = new int[limits.HistoryEntryCapacity];
        _plans = new PlannedChange[entities];
        _planAvailableSlots = new int[entities * 2];
        _planReuseStamps = new uint[entities];
        _affectedKeys = new SpatialChunkKey[entities * 2];
        _affectedRevisions = new ulong[entities * 2];
        _pendingDirty = new PendingDirty[entities * 2];
        _queryIndices = new int[entities];
        _queryStamps = new uint[entities];
        _queryLatest = new HistoryEntry[entities];
        ResetFreeStorage();
    }

    /// <summary>
    /// Enables the bounded PF4E source-record census. It is deliberately independent from
    /// publication behavior so ordinary production commits pay no per-record diagnostic cost.
    /// </summary>
    public void ConfigureChangeCensus(bool enabled)
    {
        EnsureOwner();
        _changeCensusEnabled = enabled;
        _presentChanges = _removeChanges = 0;
        _transformChanges = _prefabChanges = 0;
        _orientationChanges = _stateFlagsChanges = 0;
        _completeRecords = _partialRecords = 0;
    }

    public PublicationEpoch Epoch { get; }
    public ChunkPublicationLimits Limits => _limits;

    /// <summary>
    /// Latest authoritative commit sequence. This sequence advances for every successful commit,
    /// including commits that publish no changed chunks.
    /// </summary>
    public ulong CommitSequence
    {
        get
        {
            EnsureOwner();
            return _commitSequence;
        }
    }

    /// <summary>
    /// Borrowed canonical keys affected by the latest successful commit. The span is valid only
    /// until the next <see cref="TryCommit"/> call on this store. It is shared publication work,
    /// not recipient-owned dirty state.
    /// </summary>
    public ReadOnlySpan<SpatialChunkKey> LastCommittedDirtyChunks
    {
        get
        {
            EnsureOwner();
            return _affectedKeys.AsSpan(0, _activeAffectedCount);
        }
    }

    /// <summary>
    /// Resolves the current Game-owned public presentation lineage for one live
    /// ECS entity. The returned identity is presentation scope only; it is not
    /// a durable, participant, or Network identity.
    /// </summary>
    public bool TryGetPublicEntityId(Entity entity, out WorldPublicEntityId id)
    {
        EnsureOwner();
        if (entity.IsValid && entity.Index < _entityMap.Length)
        {
            ref readonly EntityMap map = ref _entityMap[entity.Index];
            if (map.Occupied && map.Entity == entity)
            {
                ref readonly PublicSlot slot = ref _slots[map.PublicSlot];
                if (slot.Active && slot.Entity == entity)
                {
                    id = new WorldPublicEntityId(checked((uint)map.PublicSlot), slot.Generation);
                    return true;
                }
            }
        }
        id = default;
        return false;
    }

    /// <summary>
    /// Resolves one current Game-owned public presentation identity back to its live ECS entity.
    /// This is an authority-side validation seam for untrusted public-target nominations; the
    /// public identity remains non-durable and grants no interaction authority by itself.
    /// </summary>
    public bool TryResolvePublicEntity(WorldPublicEntityId id, out Entity entity)
    {
        EnsureOwner();
        if (id.IsValid && id.Index < _slots.Length)
        {
            ref readonly PublicSlot slot = ref _slots[id.Index];
            if (slot.Active && slot.Generation == id.Generation)
            {
                entity = slot.Entity;
                return entity.IsValid;
            }
        }
        entity = Entity.Null;
        return false;
    }

    public bool TryCommit(
        ReadOnlySpan<WorldPublicChange> changes,
        out ChunkPublicationCommitError error)
    {
        EnsureOwner();
        if (changes.Length > _plans.Length)
        {
            error = ChunkPublicationCommitError.NonCanonicalChanges;
            return false;
        }
        if (!TryPlan(changes, out int planCount, out error))
            return false;

        ulong nextCommit = checked(_commitSequence + 1);
        int affectedCount = CollectAffectedKeys(planCount);
        _activeAffectedCount = affectedCount;
        if (affectedCount != 0 && _revisionSequence > ulong.MaxValue - (ulong)affectedCount)
        {
            error = ChunkPublicationCommitError.RevisionExhausted;
            return false;
        }
        EnsureChunkCapacity(CountMissingAffectedChunks());

        for (int index = 0; index < affectedCount; index++)
        {
            int chunk = GetOrCreateChunk(_affectedKeys[index]);
            bool beginsIncarnation = _chunks[chunk].Revision == 0;
            ulong revision = ++_revisionSequence;
            _affectedRevisions[index] = revision;
            _chunks[chunk].Revision = revision;
            if (beginsIncarnation)
                _chunks[chunk].HistoryFloor = revision;
            _publishedChunkRevisions++;
        }

        int pendingCount = 0;
        for (int index = 0; index < planCount; index++)
            ApplyRemoval(ref _plans[index], ref pendingCount);
        for (int index = 0; index < planCount; index++)
            ApplyPresent(ref _plans[index], ref pendingCount);
        for (int index = 0; index < planCount; index++)
            ReleaseUnusedRemovedSlot(ref _plans[index]);

        for (int index = 0; index < affectedCount; index++)
        {
            int chunk = FindChunkRequired(_affectedKeys[index]);
            _chunks[chunk].EmptySinceCommit = _chunks[chunk].EntityCount == 0 ? nextCommit : 0;
        }

        CommitHistory(pendingCount);
        if (_changeCensusEnabled)
        for (int index = 0; index < pendingCount; index++)
        {
            ref readonly PendingDirty pending = ref _pendingDirty[index];
            SaturatingIncrement(ref (pending.Operation == WorldPublicOperation.Present
                ? ref _presentChanges
                : ref _removeChanges));
            if ((pending.Fields & WorldPublicFieldMask.Transform) != 0)
                SaturatingIncrement(ref _transformChanges);
            if ((pending.Fields & WorldPublicFieldMask.Prefab) != 0)
                SaturatingIncrement(ref _prefabChanges);
            if ((pending.Fields & WorldPublicFieldMask.Orientation) != 0)
                SaturatingIncrement(ref _orientationChanges);
            if ((pending.Fields & WorldPublicFieldMask.StateFlags) != 0)
                SaturatingIncrement(ref _stateFlagsChanges);
            SaturatingIncrement(ref (pending.Fields == WorldPublicFieldMask.Complete
                ? ref _completeRecords
                : ref _partialRecords));
        }
        _commitSequence = nextCommit;
        error = ChunkPublicationCommitError.None;
        return true;
    }

    public bool TryGetCommittedView(SpatialChunkKey key, out ChunkPublicationView view)
    {
        EnsureOwner();
        if (!TryFindChunk(key, out int slot))
        {
            view = default;
            return false;
        }
        ref readonly ChunkRecord chunk = ref _chunks[slot];
        view = new ChunkPublicationView(
            key,
            Epoch,
            new ChunkPublicationRevision(chunk.Revision),
            chunk.HistoryFloor == 0 ? default : new ChunkPublicationRevision(chunk.HistoryFloor),
            chunk.EntityCount,
            chunk.HeadPage,
            _limits.RowsPerPage,
            _pageNext,
            _pageCounts,
            _rowEntityIndices,
            _rowEntityGenerations,
            _rowVersions,
            _rowPrefabIds,
            _rowLocalX,
            _rowLocalZ,
            _rowY,
            _rowOrientations,
            _rowFlags,
            _rowChangedRevisions);
        return true;
    }

    public ChunkPublicationReadStatus TryCopySnapshot(
        SpatialChunkKey key,
        Span<WorldPublicRecord> destination,
        out ChunkPublicationBatch batch)
    {
        EnsureOwner();
        if (!TryFindChunk(key, out int chunkSlot))
        {
            batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.ChunkAbsent, key, default, default, 0, 0);
            return ChunkPublicationReadStatus.ChunkAbsent;
        }
        ref readonly ChunkRecord chunk = ref _chunks[chunkSlot];
        if (destination.Length < chunk.EntityCount)
        {
            batch = CreateBatch(ChunkPublicationReadStatus.DestinationTooSmall, key, chunk,
                0, chunk.EntityCount);
            return ChunkPublicationReadStatus.DestinationTooSmall;
        }

        int written = 0;
        for (int page = chunk.HeadPage; page >= 0; page = _pageNext[page])
        {
            int start = page * _limits.RowsPerPage;
            for (int row = 0; row < _pageCounts[page]; row++)
                destination[written++] = ReadRecord(start + row, key);
        }
        SortRecords(destination[..written]);
        _snapshotReads++;
        batch = CreateBatch(ChunkPublicationReadStatus.Snapshot, key, chunk, written, written);
        return ChunkPublicationReadStatus.Snapshot;
    }

    public ChunkPublicationReadStatus TryCopyDelta(
        SpatialChunkKey key,
        ChunkPublicationCursor cursor,
        Span<WorldPublicDeltaRecord> destination,
        out ChunkPublicationBatch batch)
    {
        EnsureOwner();
        if (!cursor.IsValid || cursor.Epoch != Epoch || cursor.Key != key ||
            !TryFindChunk(key, out int chunkSlot))
        {
            _snapshotFallbacks++;
            batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.SnapshotRequired, key, default, default, 0, 0);
            return ChunkPublicationReadStatus.SnapshotRequired;
        }

        ref readonly ChunkRecord chunk = ref _chunks[chunkSlot];
        if (cursor.Revision.Value > chunk.Revision)
        {
            batch = CreateBatch(ChunkPublicationReadStatus.FutureCursor, key, chunk, 0, 0);
            return ChunkPublicationReadStatus.FutureCursor;
        }
        if (cursor.Revision.Value == chunk.Revision)
        {
            batch = CreateBatch(ChunkPublicationReadStatus.NoChanges, key, chunk, 0, 0);
            return ChunkPublicationReadStatus.NoChanges;
        }
        if (cursor.Revision.Value < chunk.HistoryFloor)
        {
            _snapshotFallbacks++;
            batch = CreateBatch(ChunkPublicationReadStatus.SnapshotRequired, key, chunk, 0, 0);
            return ChunkPublicationReadStatus.SnapshotRequired;
        }

        uint stamp = NextQueryStamp();
        int count = 0;
        for (int node = chunk.HistoryHead; node >= 0; node = _history[node].NextChunk)
        {
            ref readonly HistoryEntry entry = ref _history[node];
            if (entry.Revision <= cursor.Revision.Value) continue;
            int publicIndex = checked((int)entry.Id.Index);
            if (_queryStamps[publicIndex] != stamp)
            {
                _queryStamps[publicIndex] = stamp;
                _queryIndices[count++] = publicIndex;
                _queryLatest[publicIndex] = entry;
            }
            else
            {
                HistoryEntry previous = _queryLatest[publicIndex];
                _queryLatest[publicIndex] = entry;
                if (previous.Id == entry.Id &&
                    previous.Operation == WorldPublicOperation.Present &&
                    entry.Operation == WorldPublicOperation.Present)
                    _queryLatest[publicIndex].Fields |= previous.Fields;
            }
        }

        if (destination.Length < count)
        {
            batch = CreateBatch(ChunkPublicationReadStatus.DestinationTooSmall, key, chunk, 0, count);
            return ChunkPublicationReadStatus.DestinationTooSmall;
        }
        for (int index = 0; index < count; index++)
        {
            ref readonly HistoryEntry entry = ref _queryLatest[_queryIndices[index]];
            WorldPublicRecord current = default;
            if (entry.Operation == WorldPublicOperation.Present)
            {
                int publicSlot = checked((int)entry.Id.Index);
                ref readonly PublicSlot slot = ref _slots[publicSlot];
                if (!slot.Active || slot.Generation != entry.Id.Generation ||
                    _chunks[_pageChunks[slot.Page]].Key != key)
                    throw new InvalidOperationException("Publication history no longer resolves its newest present row.");
                current = ReadRecord(slot.Page * _limits.RowsPerPage + slot.Row, key);
            }
            destination[index] = new WorldPublicDeltaRecord(
                entry.Operation, entry.Fields, entry.Id, entry.EntityVersion, current);
        }
        SortRecords(destination[..count]);
        _deltaReads++;
        batch = CreateBatch(ChunkPublicationReadStatus.Delta, key, chunk, count, count);
        return ChunkPublicationReadStatus.Delta;
    }

    public ChunkPublicationStoreSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new ChunkPublicationStoreSnapshot(
            Epoch,
            _entityCount,
            _chunkCount,
            _pageCount,
            _historyCount,
            _history.Length,
            _commitSequence,
            _publishedChunkRevisions,
            _historyInvalidations,
            _largestDirtyCommit,
            _snapshotReads,
            _deltaReads,
            _snapshotFallbacks,
            CalculateReservedBytes(),
            _presentChanges,
            _removeChanges,
            _transformChanges,
            _prefabChanges,
            _orientationChanges,
            _stateFlagsChanges,
            _completeRecords,
            _partialRecords);
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private bool TryPlan(
        ReadOnlySpan<WorldPublicChange> changes,
        out int planCount,
        out ChunkPublicationCommitError error)
    {
        planCount = 0;
        uint previousIndex = 0;
        bool hasPrevious = false;
        uint planStamp = NextPlanStamp();
        int removedAvailable = 0;
        int additions = 0;

        for (int index = 0; index < changes.Length; index++)
        {
            ref readonly WorldPublicChange change = ref changes[index];
            if (!change.Entity.IsValid || change.Entity.Index >= (uint)_entityMap.Length ||
                hasPrevious && change.Entity.Index <= previousIndex ||
                change.IsPublic && !change.Descriptor.IsValid)
            {
                error = change.IsPublic && !change.Descriptor.IsValid
                    ? ChunkPublicationCommitError.InvalidPublicState
                    : ChunkPublicationCommitError.NonCanonicalChanges;
                return false;
            }
            previousIndex = change.Entity.Index;
            hasPrevious = true;
            ref readonly EntityMap mapping = ref _entityMap[change.Entity.Index];
            bool hasOld = mapping.Occupied;
            bool sameIdentity = hasOld && mapping.Entity == change.Entity;
            int slot = hasOld ? mapping.PublicSlot : -1;
            PlannedMode mode;
            WorldPublicFieldMask fields = WorldPublicFieldMask.None;
            SpatialChunkKey source = default;
            if (hasOld)
                source = GetSlotChunk(slot);

            if (!sameIdentity && hasOld)
            {
                mode = change.IsPublic ? PlannedMode.Replace : PlannedMode.Remove;
            }
            else if (!change.IsPublic)
            {
                if (!hasOld) continue;
                mode = PlannedMode.Remove;
            }
            else if (!hasOld)
            {
                mode = PlannedMode.Add;
                additions++;
            }
            else
            {
                WorldPublicRecord current = ReadSlotRecord(slot);
                SpatialChunkKey destination = change.Position.ToChunkKey(source.Space);
                fields = CompareFields(current, change.Position, change.Descriptor);
                if (source != destination)
                {
                    mode = PlannedMode.Move;
                    fields = WorldPublicFieldMask.Complete;
                }
                else if (fields != WorldPublicFieldMask.None)
                {
                    mode = PlannedMode.Update;
                }
                else
                {
                    continue;
                }
            }

            ref PlannedChange plan = ref _plans[planCount++];
            plan = new PlannedChange(mode, change.Entity, slot, source,
                change.IsPublic ? change.Position.ToChunkKey(GameWorldState.DefaultSpace) : default,
                change.Position, change.Descriptor, fields);
            if (mode is PlannedMode.Remove or PlannedMode.Replace or PlannedMode.Move)
            {
                ref readonly PublicSlot old = ref _slots[slot];
                int increments = mode == PlannedMode.Move ? 2 : 1;
                if (old.Version > ulong.MaxValue - (ulong)increments)
                {
                    error = ChunkPublicationCommitError.IdentityExhausted;
                    return false;
                }
                plan.OldId = new WorldPublicEntityId(checked((uint)slot), old.Generation);
                plan.RemoveVersion = old.Version + 1;
                if (mode == PlannedMode.Move)
                {
                    plan.NewId = plan.OldId;
                    plan.PresentVersion = old.Version + 2;
                }
                else if (mode == PlannedMode.Replace)
                {
                    if (old.Generation == uint.MaxValue)
                    {
                        error = ChunkPublicationCommitError.IdentityExhausted;
                        return false;
                    }
                    plan.NewId = new WorldPublicEntityId(checked((uint)slot), old.Generation + 1);
                    plan.PresentVersion = 1;
                }
                else if (old.Generation != uint.MaxValue)
                {
                    _planAvailableSlots[removedAvailable++] = slot;
                }
            }
            else if (mode == PlannedMode.Update)
            {
                ref readonly PublicSlot old = ref _slots[slot];
                if (old.Version == ulong.MaxValue)
                {
                    error = ChunkPublicationCommitError.IdentityExhausted;
                    return false;
                }
                plan.NewId = new WorldPublicEntityId(checked((uint)slot), old.Generation);
                plan.PresentVersion = old.Version + 1;
            }
        }

        int existingFree = 0;
        for (int index = _freeSlotCount - 1; index >= 0; index--)
        {
            int slot = _freeSlots[index];
            if (_slots[slot].Generation == uint.MaxValue) continue;
            _planAvailableSlots[removedAvailable + existingFree++] = slot;
        }
        if (additions > removedAvailable + existingFree)
        {
            error = ChunkPublicationCommitError.IdentityExhausted;
            return false;
        }

        int available = 0;
        for (int index = 0; index < planCount; index++)
        {
            ref PlannedChange plan = ref _plans[index];
            if (plan.Mode != PlannedMode.Add) continue;
            int slot = _planAvailableSlots[available++];
            ref readonly PublicSlot candidate = ref _slots[slot];
            plan.PublicSlot = slot;
            plan.NewId = new WorldPublicEntityId(checked((uint)slot), candidate.Generation + 1);
            plan.PresentVersion = 1;
            if (available <= removedAvailable)
            {
                _planReuseStamps[slot] = planStamp;
            }
            else
            {
                plan.UsesExistingFreeSlot = true;
            }
        }

        for (int index = 0; index < planCount; index++)
        {
            ref PlannedChange plan = ref _plans[index];
            if (plan.Mode == PlannedMode.Replace)
                _planReuseStamps[plan.PublicSlot] = planStamp;
        }
        error = ChunkPublicationCommitError.None;
        return true;
    }

    private int CollectAffectedKeys(int planCount)
    {
        int count = 0;
        for (int index = 0; index < planCount; index++)
        {
            ref readonly PlannedChange plan = ref _plans[index];
            if (plan.Mode is PlannedMode.Remove or PlannedMode.Move or PlannedMode.Replace)
                _affectedKeys[count++] = plan.Source;
            if (plan.Mode is PlannedMode.Add or PlannedMode.Update or PlannedMode.Move or PlannedMode.Replace)
                _affectedKeys[count++] = plan.Destination;
        }
        if (count == 0) return 0;
        SortAffectedKeys(count);
        int unique = 1;
        for (int index = 1; index < count; index++)
            if (_affectedKeys[index] != _affectedKeys[unique - 1])
                _affectedKeys[unique++] = _affectedKeys[index];
        return unique;
    }

    private void ApplyRemoval(ref PlannedChange plan, ref int pendingCount)
    {
        if (plan.Mode is not (PlannedMode.Remove or PlannedMode.Move or PlannedMode.Replace)) return;
        int slotIndex = plan.PublicSlot;
        ref PublicSlot slot = ref _slots[slotIndex];
        RemoveRow(slotIndex);
        _entityMap[slot.Entity.Index] = default;
        slot.Version = plan.RemoveVersion;
        slot.Page = -1;
        slot.Row = -1;
        if (plan.Mode != PlannedMode.Move)
            slot.Active = false;
        _pendingDirty[pendingCount++] = new PendingDirty(
            plan.Source,
            WorldPublicOperation.Remove,
            WorldPublicFieldMask.None,
            plan.OldId,
            plan.RemoveVersion);
    }

    private void ApplyPresent(ref PlannedChange plan, ref int pendingCount)
    {
        if (plan.Mode is PlannedMode.Remove or PlannedMode.None) return;
        int slotIndex = plan.PublicSlot;
        ref PublicSlot slot = ref _slots[slotIndex];
        if (plan.Mode == PlannedMode.Add && plan.UsesExistingFreeSlot)
            PopExpectedFreeSlot(slotIndex);

        ulong revision = FindAffectedRevision(plan.Destination);
        if (plan.Mode == PlannedMode.Update)
        {
            slot.Version = plan.PresentVersion;
            WriteRow(slot.Page, slot.Row, plan.NewId, plan.PresentVersion,
                plan.Position, plan.Descriptor, revision);
        }
        else
        {
            slot.Entity = plan.Entity;
            slot.Generation = plan.NewId.Generation;
            slot.Version = plan.PresentVersion;
            slot.Active = true;
            AddRow(slotIndex, plan.Destination, plan.NewId, plan.PresentVersion,
                plan.Position, plan.Descriptor, revision);
            _entityMap[plan.Entity.Index] = new EntityMap(plan.Entity, slotIndex, true);
            if (plan.Mode == PlannedMode.Add)
                _entityCount++;
        }
        _pendingDirty[pendingCount++] = new PendingDirty(
            plan.Destination,
            WorldPublicOperation.Present,
            plan.Mode == PlannedMode.Update ? plan.Fields : WorldPublicFieldMask.Complete,
            plan.NewId,
            plan.PresentVersion);
    }

    private void ReleaseUnusedRemovedSlot(ref PlannedChange plan)
    {
        if (plan.Mode != PlannedMode.Remove) return;
        int slot = plan.PublicSlot;
        _entityCount--;
        if (_planReuseStamps[slot] != _planStamp && _slots[slot].Generation != uint.MaxValue)
            _freeSlots[_freeSlotCount++] = slot;
    }

    private void CommitHistory(int pendingCount)
    {
        _largestDirtyCommit = Math.Max(_largestDirtyCommit, pendingCount);
        if (pendingCount > _history.Length)
        {
            ClearAllHistory();
            for (int chunk = 0; chunk < _chunks.Length; chunk++)
                if (_chunks[chunk].Occupied)
                    _chunks[chunk].HistoryFloor = _chunks[chunk].Revision;
            _historyInvalidations++;
            return;
        }
        EnsureHistoryCapacity(pendingCount);
        SortPendingDirty(pendingCount);
        for (int index = 0; index < pendingCount; index++)
        {
            ref readonly PendingDirty pending = ref _pendingDirty[index];
            int chunk = FindChunkRequired(pending.Key);
            AppendHistory(chunk, new HistoryEntry(
                _chunks[chunk].Revision,
                pending.Operation,
                pending.Fields,
                pending.Id,
                pending.EntityVersion));
        }
    }

    private void EnsureHistoryCapacity(int required)
    {
        while (_freeHistoryCount < required)
        {
            if (_historyGlobalHead < 0)
                throw new InvalidOperationException("Publication history accounting lost retained entries.");
            ref readonly HistoryEntry oldest = ref _history[_historyGlobalHead];
            int chunk = oldest.ChunkSlot;
            ulong revision = oldest.Revision;
            while (_chunks[chunk].HistoryHead >= 0 &&
                   _history[_chunks[chunk].HistoryHead].Revision == revision)
                RemoveHistoryNode(_chunks[chunk].HistoryHead);
            _chunks[chunk].HistoryFloor = Math.Max(_chunks[chunk].HistoryFloor, revision);
        }
    }

    private void AppendHistory(int chunkSlot, HistoryEntry value)
    {
        int node = _freeHistory[--_freeHistoryCount];
        value.ChunkSlot = chunkSlot;
        value.PreviousGlobal = _historyGlobalTail;
        value.NextGlobal = -1;
        value.PreviousChunk = _chunks[chunkSlot].HistoryTail;
        value.NextChunk = -1;
        _history[node] = value;
        if (_historyGlobalTail >= 0) _history[_historyGlobalTail].NextGlobal = node;
        else _historyGlobalHead = node;
        _historyGlobalTail = node;
        if (_chunks[chunkSlot].HistoryTail >= 0) _history[_chunks[chunkSlot].HistoryTail].NextChunk = node;
        else _chunks[chunkSlot].HistoryHead = node;
        _chunks[chunkSlot].HistoryTail = node;
        _historyCount++;
    }

    private void RemoveHistoryNode(int node)
    {
        HistoryEntry entry = _history[node];
        if (entry.PreviousGlobal >= 0) _history[entry.PreviousGlobal].NextGlobal = entry.NextGlobal;
        else _historyGlobalHead = entry.NextGlobal;
        if (entry.NextGlobal >= 0) _history[entry.NextGlobal].PreviousGlobal = entry.PreviousGlobal;
        else _historyGlobalTail = entry.PreviousGlobal;
        ref ChunkRecord chunk = ref _chunks[entry.ChunkSlot];
        if (entry.PreviousChunk >= 0) _history[entry.PreviousChunk].NextChunk = entry.NextChunk;
        else chunk.HistoryHead = entry.NextChunk;
        if (entry.NextChunk >= 0) _history[entry.NextChunk].PreviousChunk = entry.PreviousChunk;
        else chunk.HistoryTail = entry.PreviousChunk;
        _history[node] = default;
        _freeHistory[_freeHistoryCount++] = node;
        _historyCount--;
    }

    private void ClearAllHistory()
    {
        for (int chunk = 0; chunk < _chunks.Length; chunk++)
        {
            _chunks[chunk].HistoryHead = -1;
            _chunks[chunk].HistoryTail = -1;
        }
        Array.Clear(_history);
        _freeHistoryCount = _history.Length;
        for (int index = 0; index < _history.Length; index++)
            _freeHistory[index] = _history.Length - 1 - index;
        _historyGlobalHead = -1;
        _historyGlobalTail = -1;
        _historyCount = 0;
    }

    private void AddRow(
        int publicSlot,
        SpatialChunkKey key,
        WorldPublicEntityId id,
        ulong version,
        in WorldPosition position,
        in WorldPublicDescriptor descriptor,
        ulong revision)
    {
        int chunkSlot = FindChunkRequired(key);
        ref ChunkRecord chunk = ref _chunks[chunkSlot];
        int page = chunk.TailPage;
        if (page < 0 || _pageCounts[page] == _limits.RowsPerPage)
        {
            if (_freePageCount == 0)
                throw new InvalidOperationException("Publication page capacity violated its admitted entity bound.");
            page = _freePages[--_freePageCount];
            _pageNext[page] = -1;
            _pagePrevious[page] = chunk.TailPage;
            _pageCounts[page] = 0;
            _pageChunks[page] = chunkSlot;
            if (chunk.TailPage >= 0) _pageNext[chunk.TailPage] = page;
            else chunk.HeadPage = page;
            chunk.TailPage = page;
            _pageCount++;
        }
        int row = _pageCounts[page]++;
        WriteRow(page, row, id, version, position, descriptor, revision);
        ref PublicSlot slot = ref _slots[publicSlot];
        slot.Page = page;
        slot.Row = row;
        chunk.EntityCount++;
    }

    private void RemoveRow(int publicSlot)
    {
        ref PublicSlot removedSlot = ref _slots[publicSlot];
        int removedPage = removedSlot.Page;
        int removedRow = removedSlot.Row;
        int chunkSlot = _pageChunks[removedPage];
        ref ChunkRecord chunk = ref _chunks[chunkSlot];
        int donorPage = chunk.TailPage;
        int donorRow = _pageCounts[donorPage] - 1;
        if (removedPage != donorPage || removedRow != donorRow)
        {
            CopyRow(donorPage, donorRow, removedPage, removedRow);
            int donorOffset = donorPage * _limits.RowsPerPage + donorRow;
            int donorSlot = checked((int)_rowEntityIndices[donorOffset]);
            _slots[donorSlot].Page = removedPage;
            _slots[donorSlot].Row = removedRow;
        }
        ClearRow(donorPage, donorRow);
        _pageCounts[donorPage]--;
        chunk.EntityCount--;
        if (_pageCounts[donorPage] == 0)
        {
            int previous = _pagePrevious[donorPage];
            if (previous >= 0) _pageNext[previous] = -1;
            else chunk.HeadPage = -1;
            chunk.TailPage = previous;
            _pageNext[donorPage] = -1;
            _pagePrevious[donorPage] = -1;
            _pageChunks[donorPage] = -1;
            _freePages[_freePageCount++] = donorPage;
            _pageCount--;
        }
    }

    private void WriteRow(
        int page,
        int row,
        WorldPublicEntityId id,
        ulong version,
        in WorldPosition position,
        in WorldPublicDescriptor descriptor,
        ulong revision)
    {
        int offset = page * _limits.RowsPerPage + row;
        _rowEntityIndices[offset] = id.Index;
        _rowEntityGenerations[offset] = id.Generation;
        _rowVersions[offset] = version;
        _rowPrefabIds[offset] = descriptor.PrefabId;
        _rowLocalX[offset] = position.Local.XMillimeters;
        _rowLocalZ[offset] = position.Local.ZMillimeters;
        _rowY[offset] = position.YMillimeters;
        _rowOrientations[offset] = descriptor.Orientation.Units;
        _rowFlags[offset] = (ushort)descriptor.Flags;
        _rowChangedRevisions[offset] = revision;
    }

    private void CopyRow(int sourcePage, int sourceRow, int destinationPage, int destinationRow)
    {
        int source = sourcePage * _limits.RowsPerPage + sourceRow;
        int destination = destinationPage * _limits.RowsPerPage + destinationRow;
        _rowEntityIndices[destination] = _rowEntityIndices[source];
        _rowEntityGenerations[destination] = _rowEntityGenerations[source];
        _rowVersions[destination] = _rowVersions[source];
        _rowPrefabIds[destination] = _rowPrefabIds[source];
        _rowLocalX[destination] = _rowLocalX[source];
        _rowLocalZ[destination] = _rowLocalZ[source];
        _rowY[destination] = _rowY[source];
        _rowOrientations[destination] = _rowOrientations[source];
        _rowFlags[destination] = _rowFlags[source];
        _rowChangedRevisions[destination] = _rowChangedRevisions[source];
    }

    private void ClearRow(int page, int row)
    {
        int offset = page * _limits.RowsPerPage + row;
        _rowEntityIndices[offset] = 0;
        _rowEntityGenerations[offset] = 0;
        _rowVersions[offset] = 0;
        _rowPrefabIds[offset] = 0;
        _rowLocalX[offset] = 0;
        _rowLocalZ[offset] = 0;
        _rowY[offset] = 0;
        _rowOrientations[offset] = 0;
        _rowFlags[offset] = 0;
        _rowChangedRevisions[offset] = 0;
    }

    private WorldPublicRecord ReadSlotRecord(int slot)
    {
        ref readonly PublicSlot value = ref _slots[slot];
        return ReadRecord(value.Page * _limits.RowsPerPage + value.Row, GetSlotChunk(slot));
    }

    private WorldPublicRecord ReadRecord(int offset, SpatialChunkKey key) => new(
        new WorldPublicEntityId(_rowEntityIndices[offset], _rowEntityGenerations[offset]),
        _rowVersions[offset],
        _rowPrefabIds[offset],
        new WorldPosition(key.Coord,
            new WorldLocalPosition(_rowLocalX[offset], _rowLocalZ[offset], _rowY[offset])),
        new WorldYaw(_rowOrientations[offset]),
        (WorldPublicStateFlags)_rowFlags[offset],
        new ChunkPublicationRevision(_rowChangedRevisions[offset]));

    private static WorldPublicFieldMask CompareFields(
        in WorldPublicRecord current,
        in WorldPosition position,
        in WorldPublicDescriptor descriptor)
    {
        WorldPublicFieldMask fields = WorldPublicFieldMask.None;
        if (current.Position != position) fields |= WorldPublicFieldMask.Transform;
        if (current.PrefabId != descriptor.PrefabId) fields |= WorldPublicFieldMask.Prefab;
        if (current.Orientation != descriptor.Orientation) fields |= WorldPublicFieldMask.Orientation;
        if (current.StateFlags != descriptor.Flags) fields |= WorldPublicFieldMask.StateFlags;
        return fields;
    }

    private int CountMissingAffectedChunks()
    {
        int missing = 0;
        for (int index = 0; index < _activeAffectedCount; index++)
            if (!TryFindChunk(_affectedKeys[index], out _))
                missing++;
        return missing;
    }

    private void EnsureChunkCapacity(int missing)
    {
        int required = missing - _freeChunkCount;
        if (required <= 0) return;
        for (int slot = 0; slot < _chunks.Length && required != 0; slot++)
        {
            ref ChunkRecord chunk = ref _chunks[slot];
            if (!chunk.Occupied || chunk.EntityCount != 0 || IsAffectedKey(chunk.Key))
                continue;
            bool invalidatedHistory = chunk.HistoryHead >= 0;
            while (chunk.HistoryHead >= 0) RemoveHistoryNode(chunk.HistoryHead);
            RemoveMap(chunk.Key);
            chunk = default;
            chunk.HeadPage = -1;
            chunk.TailPage = -1;
            chunk.HistoryHead = -1;
            chunk.HistoryTail = -1;
            _freeChunks[_freeChunkCount++] = slot;
            _chunkCount--;
            if (invalidatedHistory) _historyInvalidations++;
            required--;
        }
        if (required != 0)
            throw new InvalidOperationException(
                "Publication chunk capacity violated its current-plus-destination guarantee.");
    }

    private bool IsAffectedKey(SpatialChunkKey key)
    {
        int low = 0;
        int high = _activeAffectedCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = _affectedKeys[middle].CompareTo(key);
            if (order == 0) return true;
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        return false;
    }

    private int GetOrCreateChunk(SpatialChunkKey key)
    {
        if (TryFindChunk(key, out int slot)) return slot;
        if (_freeChunkCount == 0)
            throw new InvalidOperationException("Publication chunk capacity violated its two-address-per-entity guarantee.");
        slot = _freeChunks[--_freeChunkCount];
        _chunks[slot] = new ChunkRecord(key);
        if (!TryAddMap(key, slot))
            throw new InvalidOperationException("Publication chunk lookup capacity was exhausted.");
        _chunkCount++;
        return slot;
    }

    private bool TryFindChunk(SpatialChunkKey key, out int slot)
    {
        int bucket = key.GetHashCode() & _mapMask;
        for (int probe = 0; probe < _mapStates.Length; probe++)
        {
            byte state = _mapStates[bucket];
            if (state == 0) break;
            if (state == 1 && _mapKeys[bucket] == key)
            {
                slot = _mapValues[bucket];
                return true;
            }
            bucket = (bucket + 1) & _mapMask;
        }
        slot = -1;
        return false;
    }

    private int FindChunkRequired(SpatialChunkKey key) => TryFindChunk(key, out int slot)
        ? slot
        : throw new InvalidOperationException("Publication chunk lookup lost committed storage.");

    private bool TryAddMap(SpatialChunkKey key, int slot)
    {
        int bucket = key.GetHashCode() & _mapMask;
        for (int probe = 0; probe < _mapStates.Length; probe++)
        {
            byte state = _mapStates[bucket];
            if (state == 1)
            {
                if (_mapKeys[bucket] == key) return false;
            }
            else
            {
                WriteMap(bucket, key, slot);
                return true;
            }
            bucket = (bucket + 1) & _mapMask;
        }
        return false;
    }

    private void RemoveMap(SpatialChunkKey key)
    {
        int bucket = key.GetHashCode() & _mapMask;
        for (int probe = 0; probe < _mapStates.Length; probe++)
        {
            byte state = _mapStates[bucket];
            if (state == 0) break;
            if (state == 1 && _mapKeys[bucket] == key)
            {
                CloseMapDeletionGap(bucket);
                return;
            }
            bucket = (bucket + 1) & _mapMask;
        }
        throw new InvalidOperationException("Publication chunk lookup and storage diverged.");
    }

    private void CloseMapDeletionGap(int hole)
    {
        int scan = (hole + 1) & _mapMask;
        while (_mapStates[scan] != 0)
        {
            int home = _mapKeys[scan].GetHashCode() & _mapMask;
            if (((hole - home) & _mapMask) < ((scan - home) & _mapMask))
            {
                _mapKeys[hole] = _mapKeys[scan];
                _mapValues[hole] = _mapValues[scan];
                _mapStates[hole] = 1;
                hole = scan;
            }
            scan = (scan + 1) & _mapMask;
        }
        _mapKeys[hole] = default;
        _mapValues[hole] = 0;
        _mapStates[hole] = 0;
    }

    private void WriteMap(int bucket, SpatialChunkKey key, int slot)
    {
        _mapKeys[bucket] = key;
        _mapValues[bucket] = slot;
        _mapStates[bucket] = 1;
    }

    private SpatialChunkKey GetSlotChunk(int slot)
    {
        ref readonly PublicSlot value = ref _slots[slot];
        if (value.Page < 0)
            throw new InvalidOperationException("A publication slot has no current page.");
        return _chunks[_pageChunks[value.Page]].Key;
    }

    private ulong FindAffectedRevision(SpatialChunkKey key)
    {
        int low = 0;
        int high = _activeAffectedCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = _affectedKeys[middle].CompareTo(key);
            if (order == 0) return _affectedRevisions[middle];
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        throw new InvalidOperationException("Publication mutation has no staged chunk revision.");
    }

    private void PopExpectedFreeSlot(int expected)
    {
        while (_freeSlotCount != 0)
        {
            int slot = _freeSlots[--_freeSlotCount];
            if (_slots[slot].Generation == uint.MaxValue) continue;
            if (slot != expected)
                throw new InvalidOperationException("Publication free-slot planning and commit order diverged.");
            return;
        }
        throw new InvalidOperationException("Publication free-slot capacity disappeared after preflight.");
    }

    private ChunkPublicationBatch CreateBatch(
        ChunkPublicationReadStatus status,
        SpatialChunkKey key,
        in ChunkRecord chunk,
        int count,
        int required) => new(
            status,
            key,
            new ChunkPublicationCursor(Epoch, key, new ChunkPublicationRevision(chunk.Revision)),
            chunk.HistoryFloor == 0 ? default : new ChunkPublicationRevision(chunk.HistoryFloor),
            count,
            required);

    private uint NextPlanStamp()
    {
        if (_planStamp == uint.MaxValue)
        {
            Array.Clear(_planReuseStamps);
            _planStamp = 1;
        }
        else _planStamp++;
        return _planStamp;
    }

    private uint NextQueryStamp()
    {
        if (_queryStamp == uint.MaxValue)
        {
            Array.Clear(_queryStamps);
            _queryStamp = 1;
        }
        else _queryStamp++;
        return _queryStamp;
    }

    private void ResetFreeStorage()
    {
        _freeSlotCount = _freeSlots.Length;
        for (int index = 0; index < _freeSlots.Length; index++)
        {
            _freeSlots[index] = _freeSlots.Length - 1 - index;
            _slots[index].Page = -1;
            _slots[index].Row = -1;
        }
        _freeChunkCount = _freeChunks.Length;
        for (int index = 0; index < _freeChunks.Length; index++)
        {
            _freeChunks[index] = _freeChunks.Length - 1 - index;
            _chunks[index].HeadPage = -1;
            _chunks[index].TailPage = -1;
            _chunks[index].HistoryHead = -1;
            _chunks[index].HistoryTail = -1;
        }
        _freePageCount = _freePages.Length;
        for (int index = 0; index < _freePages.Length; index++)
        {
            _freePages[index] = _freePages.Length - 1 - index;
            _pageNext[index] = -1;
            _pagePrevious[index] = -1;
            _pageChunks[index] = -1;
        }
        _freeHistoryCount = _freeHistory.Length;
        for (int index = 0; index < _freeHistory.Length; index++)
            _freeHistory[index] = _freeHistory.Length - 1 - index;
    }

    private long CalculateReservedBytes() => checked(
        (long)_entityMap.Length * Unsafe.SizeOf<EntityMap>() +
        (long)_slots.Length * Unsafe.SizeOf<PublicSlot>() +
        (long)_freeSlots.Length * sizeof(int) +
        (long)_chunks.Length * Unsafe.SizeOf<ChunkRecord>() +
        (long)_freeChunks.Length * sizeof(int) +
        (long)_mapKeys.Length * Unsafe.SizeOf<SpatialChunkKey>() +
        (long)_mapValues.Length * sizeof(int) + _mapStates.Length +
        (long)(_pageNext.Length + _pagePrevious.Length + _pageCounts.Length + _pageChunks.Length +
            _freePages.Length) * sizeof(int) +
        (long)(_rowEntityIndices.Length + _rowPrefabIds.Length) * sizeof(uint) +
        (long)_rowEntityGenerations.Length * sizeof(uint) +
        (long)(_rowVersions.Length + _rowChangedRevisions.Length) * sizeof(ulong) +
        (long)(_rowLocalX.Length + _rowLocalZ.Length + _rowOrientations.Length + _rowFlags.Length) * sizeof(ushort) +
        (long)_rowY.Length * sizeof(int) +
        (long)_history.Length * Unsafe.SizeOf<HistoryEntry>() +
        (long)_freeHistory.Length * sizeof(int) +
        (long)_plans.Length * Unsafe.SizeOf<PlannedChange>() +
        (long)_planAvailableSlots.Length * sizeof(int) +
        (long)_planReuseStamps.Length * sizeof(uint) +
        (long)_affectedKeys.Length * Unsafe.SizeOf<SpatialChunkKey>() +
        (long)_affectedRevisions.Length * sizeof(ulong) +
        (long)_pendingDirty.Length * Unsafe.SizeOf<PendingDirty>() +
        (long)_queryIndices.Length * sizeof(int) +
        (long)_queryStamps.Length * sizeof(uint) +
        (long)_queryLatest.Length * Unsafe.SizeOf<HistoryEntry>());

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Chunk publication is owner-thread bound.");
    }

    private void SortAffectedKeys(int count)
    {
        Span<SpatialChunkKey> keys = _affectedKeys.AsSpan(0, count);
        for (int root = count / 2 - 1; root >= 0; root--)
            SiftDown(keys, root, count);
        for (int end = count - 1; end > 0; end--)
        {
            (keys[0], keys[end]) = (keys[end], keys[0]);
            SiftDown(keys, 0, end);
        }
    }

    private void SortPendingDirty(int count)
    {
        Span<PendingDirty> values = _pendingDirty.AsSpan(0, count);
        for (int root = count / 2 - 1; root >= 0; root--)
            SiftDown(values, root, count);
        for (int end = count - 1; end > 0; end--)
        {
            (values[0], values[end]) = (values[end], values[0]);
            SiftDown(values, 0, end);
        }
    }

    private static void SiftDown(Span<PendingDirty> values, int root, int count)
    {
        while (true)
        {
            int child = checked(root * 2 + 1);
            if (child >= count) return;
            if (child + 1 < count && Compare(values[child], values[child + 1]) < 0) child++;
            if (Compare(values[root], values[child]) >= 0) return;
            (values[root], values[child]) = (values[child], values[root]);
            root = child;
        }
    }

    private static int Compare(in PendingDirty left, in PendingDirty right)
    {
        int key = left.Key.CompareTo(right.Key);
        return key != 0 ? key : left.Id.CompareTo(right.Id);
    }

    private static void SiftDown(Span<SpatialChunkKey> keys, int root, int count)
    {
        while (true)
        {
            int child = checked(root * 2 + 1);
            if (child >= count) return;
            if (child + 1 < count && keys[child].CompareTo(keys[child + 1]) < 0) child++;
            if (keys[root].CompareTo(keys[child]) >= 0) return;
            (keys[root], keys[child]) = (keys[child], keys[root]);
            root = child;
        }
    }

    private static void SortRecords(Span<WorldPublicRecord> records)
    {
        for (int root = records.Length / 2 - 1; root >= 0; root--)
            SiftDown(records, root, records.Length);
        for (int end = records.Length - 1; end > 0; end--)
        {
            (records[0], records[end]) = (records[end], records[0]);
            SiftDown(records, 0, end);
        }
    }

    private static void SiftDown(Span<WorldPublicRecord> records, int root, int count)
    {
        while (true)
        {
            int child = checked(root * 2 + 1);
            if (child >= count) return;
            if (child + 1 < count && records[child].Id.CompareTo(records[child + 1].Id) < 0)
                child++;
            if (records[root].Id.CompareTo(records[child].Id) >= 0) return;
            (records[root], records[child]) = (records[child], records[root]);
            root = child;
        }
    }

    private static void SortRecords(Span<WorldPublicDeltaRecord> records)
    {
        for (int root = records.Length / 2 - 1; root >= 0; root--)
            SiftDown(records, root, records.Length);
        for (int end = records.Length - 1; end > 0; end--)
        {
            (records[0], records[end]) = (records[end], records[0]);
            SiftDown(records, 0, end);
        }
    }

    private static void SiftDown(Span<WorldPublicDeltaRecord> records, int root, int count)
    {
        while (true)
        {
            int child = checked(root * 2 + 1);
            if (child >= count) return;
            if (child + 1 < count && records[child].Id.CompareTo(records[child + 1].Id) < 0)
                child++;
            if (records[root].Id.CompareTo(records[child].Id) >= 0) return;
            (records[root], records[child]) = (records[child], records[root]);
            root = child;
        }
    }

    private enum PlannedMode : byte { None, Add, Update, Move, Remove, Replace }

    private struct PlannedChange
    {
        internal PlannedChange(
            PlannedMode mode,
            Entity entity,
            int publicSlot,
            SpatialChunkKey source,
            SpatialChunkKey destination,
            WorldPosition position,
            WorldPublicDescriptor descriptor,
            WorldPublicFieldMask fields)
        {
            Mode = mode;
            Entity = entity;
            PublicSlot = publicSlot;
            Source = source;
            Destination = destination;
            Position = position;
            Descriptor = descriptor;
            Fields = fields;
        }
        internal PlannedMode Mode;
        internal Entity Entity;
        internal int PublicSlot;
        internal SpatialChunkKey Source;
        internal SpatialChunkKey Destination;
        internal WorldPosition Position;
        internal WorldPublicDescriptor Descriptor;
        internal WorldPublicFieldMask Fields;
        internal WorldPublicEntityId OldId;
        internal WorldPublicEntityId NewId;
        internal ulong RemoveVersion;
        internal ulong PresentVersion;
        internal bool UsesExistingFreeSlot;
    }

    private struct EntityMap
    {
        internal EntityMap(Entity entity, int publicSlot, bool occupied) =>
            (Entity, PublicSlot, Occupied) = (entity, publicSlot, occupied);
        internal Entity Entity;
        internal int PublicSlot;
        internal bool Occupied;
    }

    private struct PublicSlot
    {
        internal Entity Entity;
        internal uint Generation;
        internal ulong Version;
        internal int Page;
        internal int Row;
        internal bool Active;
    }

    private struct ChunkRecord
    {
        internal ChunkRecord(SpatialChunkKey key)
        {
            Key = key;
            HeadPage = -1;
            TailPage = -1;
            HistoryHead = -1;
            HistoryTail = -1;
            Occupied = true;
        }
        internal SpatialChunkKey Key;
        internal int HeadPage;
        internal int TailPage;
        internal int EntityCount;
        internal ulong Revision;
        internal ulong HistoryFloor;
        internal ulong EmptySinceCommit;
        internal int HistoryHead;
        internal int HistoryTail;
        internal bool Occupied;
    }

    private struct HistoryEntry
    {
        internal HistoryEntry(
            ulong revision,
            WorldPublicOperation operation,
            WorldPublicFieldMask fields,
            WorldPublicEntityId id,
            ulong entityVersion)
        {
            Revision = revision;
            Operation = operation;
            Fields = fields;
            Id = id;
            EntityVersion = entityVersion;
            ChunkSlot = -1;
            PreviousGlobal = -1;
            NextGlobal = -1;
            PreviousChunk = -1;
            NextChunk = -1;
        }
        internal ulong Revision;
        internal WorldPublicOperation Operation;
        internal WorldPublicFieldMask Fields;
        internal WorldPublicEntityId Id;
        internal ulong EntityVersion;
        internal int ChunkSlot;
        internal int PreviousGlobal;
        internal int NextGlobal;
        internal int PreviousChunk;
        internal int NextChunk;
    }

    private readonly record struct PendingDirty(
        SpatialChunkKey Key,
        WorldPublicOperation Operation,
        WorldPublicFieldMask Fields,
        WorldPublicEntityId Id,
        ulong EntityVersion);

}

/// <summary>Borrowed SoA rows from one packed publication page.</summary>
public readonly ref struct ChunkPublicationPageView
{
    internal ChunkPublicationPageView(
        ReadOnlySpan<uint> entityIndices,
        ReadOnlySpan<uint> entityGenerations,
        ReadOnlySpan<ulong> versions,
        ReadOnlySpan<uint> prefabIds,
        ReadOnlySpan<ushort> localX,
        ReadOnlySpan<ushort> localZ,
        ReadOnlySpan<int> y,
        ReadOnlySpan<ushort> orientations,
        ReadOnlySpan<ushort> flags,
        ReadOnlySpan<ulong> changedRevisions)
    {
        EntityIndices = entityIndices;
        EntityGenerations = entityGenerations;
        EntityPublicVersions = versions;
        PrefabIds = prefabIds;
        LocalXMillimeters = localX;
        LocalZMillimeters = localZ;
        YMillimeters = y;
        OrientationUnits = orientations;
        StateFlags = flags;
        LastChangedRevisions = changedRevisions;
    }

    public ReadOnlySpan<uint> EntityIndices { get; }
    public ReadOnlySpan<uint> EntityGenerations { get; }
    public ReadOnlySpan<ulong> EntityPublicVersions { get; }
    public ReadOnlySpan<uint> PrefabIds { get; }
    public ReadOnlySpan<ushort> LocalXMillimeters { get; }
    public ReadOnlySpan<ushort> LocalZMillimeters { get; }
    public ReadOnlySpan<int> YMillimeters { get; }
    public ReadOnlySpan<ushort> OrientationUnits { get; }
    public ReadOnlySpan<ushort> StateFlags { get; }
    public ReadOnlySpan<ulong> LastChangedRevisions { get; }
    public int Count => EntityIndices.Length;
}

/// <summary>
/// Borrowed committed view. It must not survive a publication mutation or generation replacement.
/// </summary>
public readonly ref struct ChunkPublicationView
{
    private readonly int _headPage;
    private readonly int _rowsPerPage;
    private readonly ReadOnlySpan<int> _pageNext;
    private readonly ReadOnlySpan<int> _pageCounts;
    private readonly ReadOnlySpan<uint> _entityIndices;
    private readonly ReadOnlySpan<uint> _entityGenerations;
    private readonly ReadOnlySpan<ulong> _versions;
    private readonly ReadOnlySpan<uint> _prefabIds;
    private readonly ReadOnlySpan<ushort> _localX;
    private readonly ReadOnlySpan<ushort> _localZ;
    private readonly ReadOnlySpan<int> _y;
    private readonly ReadOnlySpan<ushort> _orientations;
    private readonly ReadOnlySpan<ushort> _flags;
    private readonly ReadOnlySpan<ulong> _changedRevisions;

    internal ChunkPublicationView(
        SpatialChunkKey key,
        PublicationEpoch epoch,
        ChunkPublicationRevision revision,
        ChunkPublicationRevision historyFloor,
        int count,
        int headPage,
        int rowsPerPage,
        ReadOnlySpan<int> pageNext,
        ReadOnlySpan<int> pageCounts,
        ReadOnlySpan<uint> entityIndices,
        ReadOnlySpan<uint> entityGenerations,
        ReadOnlySpan<ulong> versions,
        ReadOnlySpan<uint> prefabIds,
        ReadOnlySpan<ushort> localX,
        ReadOnlySpan<ushort> localZ,
        ReadOnlySpan<int> y,
        ReadOnlySpan<ushort> orientations,
        ReadOnlySpan<ushort> flags,
        ReadOnlySpan<ulong> changedRevisions)
    {
        Key = key;
        Epoch = epoch;
        Revision = revision;
        HistoryFloor = historyFloor;
        Count = count;
        _headPage = headPage;
        _rowsPerPage = rowsPerPage;
        _pageNext = pageNext;
        _pageCounts = pageCounts;
        _entityIndices = entityIndices;
        _entityGenerations = entityGenerations;
        _versions = versions;
        _prefabIds = prefabIds;
        _localX = localX;
        _localZ = localZ;
        _y = y;
        _orientations = orientations;
        _flags = flags;
        _changedRevisions = changedRevisions;
    }

    public SpatialChunkKey Key { get; }
    public PublicationEpoch Epoch { get; }
    public ChunkPublicationRevision Revision { get; }
    public ChunkPublicationRevision HistoryFloor { get; }
    public int Count { get; }
    public Enumerator GetEnumerator() => new(this);

    public ref struct Enumerator
    {
        private readonly ChunkPublicationView _view;
        private int _page;
        private ChunkPublicationPageView _current;

        internal Enumerator(ChunkPublicationView view)
        {
            _view = view;
            _page = -2;
            _current = default;
        }

        public readonly ChunkPublicationPageView Current => _current;

        public bool MoveNext()
        {
            _page = _page == -2 ? _view._headPage : _page < 0 ? -1 : _view._pageNext[_page];
            if (_page < 0) return false;
            int count = _view._pageCounts[_page];
            int start = _page * _view._rowsPerPage;
            _current = new ChunkPublicationPageView(
                _view._entityIndices.Slice(start, count),
                _view._entityGenerations.Slice(start, count),
                _view._versions.Slice(start, count),
                _view._prefabIds.Slice(start, count),
                _view._localX.Slice(start, count),
                _view._localZ.Slice(start, count),
                _view._y.Slice(start, count),
                _view._orientations.Slice(start, count),
                _view._flags.Slice(start, count),
                _view._changedRevisions.Slice(start, count));
            return true;
        }
    }
}

/// <summary>
/// Canonical engine-neutral snapshot-body encoding for prepared publication experiments.
/// Records must be the sorted output of <see cref="ChunkPublicationStore.TryCopySnapshot"/>.
/// </summary>
public static class ChunkPublicationSemanticCodec
{
    public const int HeaderBytes = 48;
    public const int RecordBytes = 40;

    public static int MeasureSnapshot(int recordCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);
        return checked(HeaderBytes + recordCount * RecordBytes);
    }

    public static bool TryWriteSnapshot(
        in ChunkPublicationBatch batch,
        ReadOnlySpan<WorldPublicRecord> records,
        Span<byte> destination,
        out int written)
    {
        written = 0;
        if (batch.Status != ChunkPublicationReadStatus.Snapshot ||
            !batch.Cursor.IsValid || batch.Cursor.Key != batch.Key ||
            !batch.Key.Space.IsValid || batch.Count != records.Length)
            return false;
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly WorldPublicRecord record = ref records[index];
            if (!record.Id.IsValid || record.EntityPublicVersion == 0 || record.PrefabId == 0 ||
                record.Position.Chunk != batch.Key.Coord ||
                (record.StateFlags & ~WorldPublicDescriptor.AdmittedFlags) != 0 ||
                ((record.StateFlags & WorldPublicStateFlags.GroundItem) != 0 &&
                 (record.StateFlags & WorldPublicStateFlags.Visible) == 0) ||
                !record.LastChangedRevision.IsValid ||
                record.LastChangedRevision.Value > batch.Cursor.Revision.Value ||
                index != 0 && records[index - 1].Id.CompareTo(record.Id) >= 0)
                return false;
        }

        int required = MeasureSnapshot(records.Length);
        if (destination.Length < required) return false;
        if (MemoryMarshal.AsBytes(records).Overlaps(destination)) return false;
        Span<byte> staged = destination[..required];
        staged.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(staged, 0x3142_5043U); // CPB1
        BinaryPrimitives.WriteUInt64LittleEndian(staged[8..], batch.Cursor.Epoch.Value);
        BinaryPrimitives.WriteUInt64LittleEndian(staged[16..], batch.Key.Space.Value);
        BinaryPrimitives.WriteInt32LittleEndian(staged[24..], batch.Key.Coord.X);
        BinaryPrimitives.WriteInt32LittleEndian(staged[28..], batch.Key.Coord.Z);
        BinaryPrimitives.WriteUInt64LittleEndian(staged[32..], batch.Cursor.Revision.Value);
        BinaryPrimitives.WriteInt32LittleEndian(staged[40..], records.Length);
        BinaryPrimitives.WriteInt32LittleEndian(staged[44..], RecordBytes);
        int offset = HeaderBytes;
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly WorldPublicRecord record = ref records[index];
            Span<byte> target = staged.Slice(offset, RecordBytes);
            BinaryPrimitives.WriteUInt32LittleEndian(target, record.Id.Index);
            BinaryPrimitives.WriteUInt32LittleEndian(target[4..], record.Id.Generation);
            BinaryPrimitives.WriteUInt64LittleEndian(target[8..], record.EntityPublicVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(target[16..], record.PrefabId);
            BinaryPrimitives.WriteUInt16LittleEndian(target[20..], record.Position.Local.XMillimeters);
            BinaryPrimitives.WriteUInt16LittleEndian(target[22..], record.Position.Local.ZMillimeters);
            BinaryPrimitives.WriteInt32LittleEndian(target[24..], record.Position.YMillimeters);
            BinaryPrimitives.WriteUInt16LittleEndian(target[28..], record.Orientation.Units);
            BinaryPrimitives.WriteUInt16LittleEndian(target[30..], (ushort)record.StateFlags);
            BinaryPrimitives.WriteUInt64LittleEndian(target[32..], record.LastChangedRevision.Value);
            offset += RecordBytes;
        }
        written = required;
        return true;
    }
}
