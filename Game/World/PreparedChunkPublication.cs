using System.Diagnostics;
using System.Runtime.CompilerServices;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>
/// Internal comparison selection. Normal construction always selects prepared latest-state
/// publication; history-derived reconstruction exists only for bounded tests and benchmarks.
/// </summary>
internal enum PreparedChunkPublicationReadModel : byte
{
    PreparedLatestState = 1,
    HistoryDerivedComparison = 2,
}

/// <summary>Cold bounded census of the selected shared source-publication store.</summary>
public readonly record struct PreparedChunkPublicationSnapshot(
    bool Selected,
    int PublicationCapacity,
    int RecordCapacity,
    int RetainedPublications,
    int RetainedRecords,
    int RetainedTombstones,
    int PublicationHighWater,
    int RecordHighWater,
    int TombstoneHighWater,
    ulong AuthoritativeSourceMutations,
    ulong DirtySourceChunks,
    ulong PreparedSourcePublications,
    ulong PreparedSourceRecords,
    ulong RecipientRangeReads,
    ulong ReferencedSourcePublications,
    ulong ReferencedSourceRecords,
    ulong HistoryDerivedSteadyStateCalls,
    ulong RetentionEvictions,
    ulong PressureEvictions,
    ulong RebaseRequirements,
    ulong SourcePreparationTicks,
    ulong RecipientRangePreparationTicks,
    ulong RecipientChainTraversalTicks,
    ulong RecipientCollapseTicks,
    ulong RecipientMaterializationTicks,
    ulong RecipientSortTicks,
    ulong RecipientSortCalls,
    ulong RecipientIndicesSorted,
    ulong RecipientTrivialSorts,
    ulong RecipientRecordsConstructed,
    int CoveragePublicationHighWater,
    long ReservedBytes);

public sealed partial class ChunkPublicationStore
{
    private PreparedChunkPublicationReadModel _preparedReadModel;
    private PreparedPublication[] _preparedPublications = [];
    private int[] _freePreparedPublications = [];
    private WorldPublicDeltaRecord[] _preparedRecords = [];
    private int[] _preparedRecordPublicationSlots = [];
    private int[] _preparedQueryNodes = [];
    private PreparedLookupKey[] _preparedLookupKeys = [];
    private int[] _preparedLookupValues = [];
    private byte[] _preparedLookupStates = [];
    private int _preparedLookupMask;
    private int _freePreparedPublicationCount;
    private int _retainedPreparedPublications;
    private int _retainedPreparedRecords;
    private int _retainedPreparedTombstones;
    private int _preparedPublicationHighWater;
    private int _preparedRecordHighWater;
    private int _preparedTombstoneHighWater;
    private ulong _preparedSourceMutations;
    private ulong _preparedDirtyChunks;
    private ulong _preparedSourcePublications;
    private ulong _preparedSourceRecords;
    private ulong _preparedRecipientRangeReads;
    private ulong _preparedPublicationReferences;
    private ulong _preparedRecordReferences;
    private ulong _historyDerivedDeltaCalls;
    private ulong _preparedRetentionEvictions;
    private ulong _preparedPressureEvictions;
    private ulong _preparedRebaseRequirements;
    private ulong _preparedSourcePreparationTicks;
    private ulong _preparedLastSourcePreparationTicks;
    private ulong _preparedRecipientPreparationTicks;
    private ulong _preparedRecipientChainTraversalTicks;
    private ulong _preparedRecipientCollapseTicks;
    private ulong _preparedRecipientMaterializationTicks;
    private ulong _preparedRecipientSortTicks;
    private ulong _preparedRecipientSortCalls;
    private ulong _preparedRecipientIndicesSorted;
    private ulong _preparedRecipientTrivialSorts;
    private ulong _preparedRecipientRecordsConstructed;
    private bool _preparedSourceAttributionEnabled;
    private bool _preparedRecipientAttributionEnabled;
    private int _preparedCoveragePublicationHighWater;

    private void InitializePreparedPublicationStorage(
        PreparedChunkPublicationReadModel readModel,
        int maximumEntities)
    {
        if (readModel is not (PreparedChunkPublicationReadModel.PreparedLatestState or
            PreparedChunkPublicationReadModel.HistoryDerivedComparison))
            throw new ArgumentOutOfRangeException(nameof(readModel));
        _preparedReadModel = readModel;
        if (readModel != PreparedChunkPublicationReadModel.PreparedLatestState) return;

        int capacity = _history.Length;
        int lookupCapacity = 1;
        int lookupTarget = checked(capacity * 2);
        while (lookupCapacity < lookupTarget) lookupCapacity = checked(lookupCapacity * 2);
        _preparedPublications = new PreparedPublication[capacity];
        _freePreparedPublications = new int[capacity];
        _preparedRecords = new WorldPublicDeltaRecord[capacity];
        _preparedRecordPublicationSlots = new int[capacity];
        _preparedQueryNodes = new int[maximumEntities];
        _preparedLookupKeys = new PreparedLookupKey[lookupCapacity];
        _preparedLookupValues = new int[lookupCapacity];
        _preparedLookupStates = new byte[lookupCapacity];
        _preparedLookupMask = lookupCapacity - 1;
        _freePreparedPublicationCount = capacity;
        for (int index = 0; index < capacity; index++)
        {
            _freePreparedPublications[index] = capacity - 1 - index;
            _preparedRecordPublicationSlots[index] = -1;
        }
    }

    /// <summary>
    /// Returns a cold immutable accounting value. The retained arrays remain internal and mutable
    /// only by the source owner; no recipient delivery or proof state is exposed here.
    /// </summary>
    public PreparedChunkPublicationSnapshot GetPreparedPublicationSnapshot()
    {
        EnsureOwner();
        return new PreparedChunkPublicationSnapshot(
            _preparedReadModel == PreparedChunkPublicationReadModel.PreparedLatestState,
            _preparedPublications.Length,
            _preparedRecords.Length,
            _retainedPreparedPublications,
            _retainedPreparedRecords,
            _retainedPreparedTombstones,
            _preparedPublicationHighWater,
            _preparedRecordHighWater,
            _preparedTombstoneHighWater,
            _preparedSourceMutations,
            _preparedDirtyChunks,
            _preparedSourcePublications,
            _preparedSourceRecords,
            _preparedRecipientRangeReads,
            _preparedPublicationReferences,
            _preparedRecordReferences,
            _historyDerivedDeltaCalls,
            _preparedRetentionEvictions,
            _preparedPressureEvictions,
            _preparedRebaseRequirements,
            _preparedSourcePreparationTicks,
            _preparedRecipientPreparationTicks,
            _preparedRecipientChainTraversalTicks,
            _preparedRecipientCollapseTicks,
            _preparedRecipientMaterializationTicks,
            _preparedRecipientSortTicks,
            _preparedRecipientSortCalls,
            _preparedRecipientIndicesSorted,
            _preparedRecipientTrivialSorts,
            _preparedRecipientRecordsConstructed,
            _preparedCoveragePublicationHighWater,
            CalculatePreparedPublicationReservedBytes());
    }

    /// <summary>
    /// Enables cold PF5-R2 stage attribution. It never changes source or recipient semantics and
    /// is disabled by default so ordinary publication does not read a clock.
    /// </summary>
    public void ConfigurePreparedPublicationAttribution(bool enabled) =>
        ConfigurePreparedPublicationAttribution(enabled, enabled);

    /// <summary>
    /// Independently enables source and recipient clocks so a benchmark can charge shared
    /// publication preparation without enabling the colder per-recipient waterfall.
    /// </summary>
    public void ConfigurePreparedPublicationAttribution(
        bool sourceEnabled,
        bool recipientEnabled)
    {
        EnsureOwner();
        _preparedSourceAttributionEnabled = sourceEnabled;
        _preparedRecipientAttributionEnabled = recipientEnabled;
        _preparedSourcePreparationTicks = 0;
        _preparedLastSourcePreparationTicks = 0;
        _preparedRecipientPreparationTicks = 0;
        _preparedRecipientChainTraversalTicks = 0;
        _preparedRecipientCollapseTicks = 0;
        _preparedRecipientMaterializationTicks = 0;
        _preparedRecipientSortTicks = 0;
        _preparedRecipientSortCalls = 0;
        _preparedRecipientIndicesSorted = 0;
        _preparedRecipientTrivialSorts = 0;
        _preparedRecipientRecordsConstructed = 0;
    }

    public ulong GetLastPreparedSourcePreparationTicks()
    {
        EnsureOwner();
        return _preparedLastSourcePreparationTicks;
    }

    private long PreparedSourceAttributionTimestamp() =>
        _preparedSourceAttributionEnabled ? Stopwatch.GetTimestamp() : 0;

    private long PreparedRecipientAttributionTimestamp() =>
        _preparedRecipientAttributionEnabled ? Stopwatch.GetTimestamp() : 0;

    private void AttributePreparedSource(long started)
    {
        if (!_preparedSourceAttributionEnabled) return;
        ulong elapsed = checked((ulong)(Stopwatch.GetTimestamp() - started));
        SaturatingAdd(ref _preparedSourcePreparationTicks, elapsed);
        SaturatingAdd(ref _preparedLastSourcePreparationTicks, elapsed);
    }

    private int BeginPreparedPublication(
        int chunkSlot,
        SpatialChunkKey key,
        ulong fromRevision,
        ulong toRevision,
        int recordCount)
    {
        if (_preparedReadModel != PreparedChunkPublicationReadModel.PreparedLatestState) return -1;
        if (recordCount <= 0 || _freePreparedPublicationCount == 0 ||
            !key.Space.IsValid || toRevision == 0 || toRevision <= fromRevision)
            throw new InvalidOperationException("Prepared publication source bounds diverged.");
        int slot = _freePreparedPublications[--_freePreparedPublicationCount];
        _preparedPublications[slot] = new PreparedPublication
        {
            Active = true,
            Key = key,
            ChunkSlot = chunkSlot,
            FromRevision = fromRevision,
            ToRevision = toRevision,
            FirstRecordNode = -1,
            LastRecordNode = -1,
            ExpectedRecords = recordCount,
        };
        AddPreparedLookup(key, fromRevision, slot);
        _retainedPreparedPublications++;
        _preparedPublicationHighWater = Math.Max(
            _preparedPublicationHighWater, _retainedPreparedPublications);
        SaturatingIncrement(ref _preparedDirtyChunks);
        SaturatingIncrement(ref _preparedSourcePublications);
        return slot;
    }

    private void AttachPreparedRecord(int publicationSlot, int historyNode, in PendingDirty pending)
    {
        if (_preparedReadModel != PreparedChunkPublicationReadModel.PreparedLatestState) return;
        ref PreparedPublication publication = ref _preparedPublications[publicationSlot];
        if (!publication.Active || publication.WrittenRecords >= publication.ExpectedRecords ||
            historyNode < 0 || historyNode >= _preparedRecords.Length)
            throw new InvalidOperationException("Prepared publication record accounting diverged.");

        WorldPublicRecord current = default;
        if (pending.Operation == WorldPublicOperation.Present)
        {
            int publicSlot = checked((int)pending.Id.Index);
            ref readonly PublicSlot slot = ref _slots[publicSlot];
            if (!slot.Active || slot.Generation != pending.Id.Generation ||
                _chunks[_pageChunks[slot.Page]].Key != pending.Key)
                throw new InvalidOperationException("Prepared positive publication lost its current source row.");
            current = ReadRecord(slot.Page * _limits.RowsPerPage + slot.Row, pending.Key);
        }
        _preparedRecords[historyNode] = new WorldPublicDeltaRecord(
            pending.Operation, pending.Fields, pending.Id, pending.EntityVersion, current);
        _preparedRecordPublicationSlots[historyNode] = publicationSlot;
        if (publication.FirstRecordNode < 0) publication.FirstRecordNode = historyNode;
        publication.LastRecordNode = historyNode;
        publication.WrittenRecords++;
        publication.RemainingRecords++;
        _retainedPreparedRecords++;
        if (pending.Operation == WorldPublicOperation.Remove)
        {
            publication.RemainingTombstones++;
            _retainedPreparedTombstones++;
        }
        _preparedRecordHighWater = Math.Max(_preparedRecordHighWater, _retainedPreparedRecords);
        _preparedTombstoneHighWater = Math.Max(
            _preparedTombstoneHighWater, _retainedPreparedTombstones);
        SaturatingIncrement(ref _preparedSourceMutations);
        SaturatingIncrement(ref _preparedSourceRecords);
    }

    private void CompletePreparedPublication(int publicationSlot)
    {
        if (_preparedReadModel != PreparedChunkPublicationReadModel.PreparedLatestState) return;
        ref readonly PreparedPublication publication = ref _preparedPublications[publicationSlot];
        if (!publication.Active || publication.WrittenRecords != publication.ExpectedRecords ||
            publication.FirstRecordNode < 0 || publication.LastRecordNode < 0)
            throw new InvalidOperationException("Prepared publication was not completely materialized.");
    }

    private ChunkPublicationReadStatus TryCopyPreparedDelta(
        SpatialChunkKey key,
        ChunkPublicationCursor cursor,
        Span<WorldPublicDeltaRecord> destination,
        out ChunkPublicationBatch batch)
    {
        EnsureOwner();
        if (!cursor.IsValid || cursor.Epoch != Epoch || cursor.Key != key ||
            !TryFindChunk(key, out int chunkSlot))
            return PreparedRebase(key, out batch);

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
            return PreparedRebase(key, in chunk, out batch);

        long preparationStarted = PreparedRecipientAttributionTimestamp();
        uint stamp = NextQueryStamp();
        int count = 0;
        int referencedPublications = 0;
        int referencedRecords = 0;
        ulong fromRevision = cursor.Revision.Value;
        while (fromRevision < chunk.Revision)
        {
            long chainStarted = PreparedRecipientAttributionTimestamp();
            int publicationSlot = FindPreparedPublication(key, fromRevision);
            if (publicationSlot < 0)
                return PreparedRebase(key, in chunk, out batch);
            ref readonly PreparedPublication publication = ref _preparedPublications[publicationSlot];
            if (!publication.Active || publication.ToRevision <= fromRevision ||
                publication.ToRevision > chunk.Revision ||
                publication.WrittenRecords != publication.ExpectedRecords)
                throw new InvalidOperationException("Prepared publication coverage chain is corrupt.");
            if (_preparedRecipientAttributionEnabled)
                SaturatingAdd(ref _preparedRecipientChainTraversalTicks,
                    checked((ulong)(Stopwatch.GetTimestamp() - chainStarted)));

            long collapseStarted = PreparedRecipientAttributionTimestamp();
            int node = publication.FirstRecordNode;
            for (int record = 0; record < publication.ExpectedRecords; record++)
            {
                if (node < 0 || node >= _history.Length ||
                    _preparedRecordPublicationSlots[node] != publicationSlot)
                    throw new InvalidOperationException("Prepared publication record chain is corrupt.");
                ref readonly HistoryEntry entry = ref _history[node];
                int publicIndex = checked((int)entry.Id.Index);
                if (_queryStamps[publicIndex] != stamp)
                {
                    _queryStamps[publicIndex] = stamp;
                    _queryIndices[count++] = publicIndex;
                    _queryLatest[publicIndex] = entry;
                    _preparedQueryNodes[publicIndex] = node;
                }
                else
                {
                    HistoryEntry previous = _queryLatest[publicIndex];
                    _queryLatest[publicIndex] = entry;
                    _preparedQueryNodes[publicIndex] = node;
                    if (previous.Id == entry.Id &&
                        previous.Operation == WorldPublicOperation.Present &&
                        entry.Operation == WorldPublicOperation.Present)
                        _queryLatest[publicIndex].Fields |= previous.Fields;
                }
                node = entry.NextChunk;
                referencedRecords++;
            }
            if (_preparedRecipientAttributionEnabled)
                SaturatingAdd(ref _preparedRecipientCollapseTicks,
                    checked((ulong)(Stopwatch.GetTimestamp() - collapseStarted)));
            fromRevision = publication.ToRevision;
            referencedPublications++;
        }
        if (fromRevision != chunk.Revision)
            throw new InvalidOperationException("Prepared publication coverage did not reach current source truth.");

        SaturatingIncrement(ref _preparedRecipientRangeReads);
        SaturatingAdd(ref _preparedPublicationReferences, checked((ulong)referencedPublications));
        SaturatingAdd(ref _preparedRecordReferences, checked((ulong)referencedRecords));
        _preparedCoveragePublicationHighWater = Math.Max(
            _preparedCoveragePublicationHighWater, referencedPublications);
        if (destination.Length < count)
        {
            batch = CreateBatch(ChunkPublicationReadStatus.DestinationTooSmall, key, chunk, 0, count);
            return ChunkPublicationReadStatus.DestinationTooSmall;
        }

        // Canonical order is by public identity. Sort the compact source indices first so each
        // 72-byte WorldPublicDeltaRecord is constructed exactly once in its final position.
        // The prior implementation materialized first and heapsorted the full records.
        long sortStarted = PreparedRecipientAttributionTimestamp();
        if (_preparedRecipientAttributionEnabled)
        {
            SaturatingIncrement(ref _preparedRecipientSortCalls);
            SaturatingAdd(ref _preparedRecipientIndicesSorted, checked((ulong)count));
            if (count <= 1) SaturatingIncrement(ref _preparedRecipientTrivialSorts);
        }
        SortPreparedIndices(_queryIndices.AsSpan(0, count));
        if (_preparedRecipientAttributionEnabled)
            SaturatingAdd(ref _preparedRecipientSortTicks,
                checked((ulong)(Stopwatch.GetTimestamp() - sortStarted)));

        long materializationStarted = PreparedRecipientAttributionTimestamp();
        for (int result = 0; result < count; result++)
        {
            int publicIndex = _queryIndices[result];
            ref readonly HistoryEntry entry = ref _queryLatest[publicIndex];
            WorldPublicRecord current = entry.Operation == WorldPublicOperation.Present
                ? _preparedRecords[_preparedQueryNodes[publicIndex]].Current
                : default;
            destination[result] = new WorldPublicDeltaRecord(
                entry.Operation, entry.Fields, entry.Id, entry.EntityVersion, current);
        }
        if (_preparedRecipientAttributionEnabled)
        {
            SaturatingAdd(ref _preparedRecipientRecordsConstructed, checked((ulong)count));
            SaturatingAdd(ref _preparedRecipientMaterializationTicks,
                checked((ulong)(Stopwatch.GetTimestamp() - materializationStarted)));
            SaturatingAdd(ref _preparedRecipientPreparationTicks,
                checked((ulong)(Stopwatch.GetTimestamp() - preparationStarted)));
        }
        _deltaReads++;
        batch = CreateBatch(ChunkPublicationReadStatus.Delta, key, chunk, count, count);
        return ChunkPublicationReadStatus.Delta;
    }

    /// <summary>
    /// Validates that one exact prepared latest-state range is still continuously retained without
    /// traversing, collapsing, sorting, or materializing its records. This is deliberately only an
    /// admission check for an immutable representation cache; it does not publish recipient
    /// knowledge or make cached bytes authoritative.
    /// </summary>
    internal bool TryInspectPreparedDeltaRange(
        SpatialChunkKey key,
        ChunkPublicationCursor cursor,
        out ChunkPublicationBatch batch)
    {
        EnsureOwner();
        batch = default;
        if (_preparedReadModel != PreparedChunkPublicationReadModel.PreparedLatestState ||
            !cursor.IsValid || cursor.Epoch != Epoch || cursor.Key != key ||
            !TryFindChunk(key, out int chunkSlot))
            return false;

        ref readonly ChunkRecord chunk = ref _chunks[chunkSlot];
        if (cursor.Revision.Value >= chunk.Revision ||
            cursor.Revision.Value < chunk.HistoryFloor)
            return false;

        ulong fromRevision = cursor.Revision.Value;
        while (fromRevision < chunk.Revision)
        {
            int publicationSlot = FindPreparedPublication(key, fromRevision);
            if (publicationSlot < 0) return false;
            ref readonly PreparedPublication publication = ref _preparedPublications[publicationSlot];
            if (!publication.Active || publication.ToRevision <= fromRevision ||
                publication.ToRevision > chunk.Revision ||
                publication.WrittenRecords != publication.ExpectedRecords)
                return false;
            fromRevision = publication.ToRevision;
        }
        if (fromRevision != chunk.Revision) return false;

        batch = CreateBatch(ChunkPublicationReadStatus.Delta, key, chunk, 0, 0);
        return true;
    }

    /// <summary>
    /// Reads only the current exact range identity for a speculative immutable-body lookup. It
    /// deliberately does not establish retained-chain coverage; every cache hit must subsequently
    /// pass <see cref="TryInspectPreparedDeltaRange"/> before it can become pending delivery state.
    /// </summary>
    internal bool TryInspectPreparedDeltaRangeIdentity(
        SpatialChunkKey key,
        ChunkPublicationCursor cursor,
        out ChunkPublicationBatch batch)
    {
        EnsureOwner();
        batch = default;
        if (_preparedReadModel != PreparedChunkPublicationReadModel.PreparedLatestState ||
            !cursor.IsValid || cursor.Epoch != Epoch || cursor.Key != key ||
            !TryFindChunk(key, out int chunkSlot))
            return false;
        ref readonly ChunkRecord chunk = ref _chunks[chunkSlot];
        if (cursor.Revision.Value >= chunk.Revision ||
            cursor.Revision.Value < chunk.HistoryFloor)
            return false;
        batch = CreateBatch(ChunkPublicationReadStatus.Delta, key, chunk, 0, 0);
        return true;
    }

    private void SortPreparedIndices(Span<int> indices)
    {
        if (indices.Length <= 1) return;
        if (indices.Length <= 8)
        {
            for (int index = 1; index < indices.Length; index++)
            {
                int value = indices[index];
                int position = index;
                while (position > 0 && ComparePreparedIndex(indices[position - 1], value) > 0)
                {
                    indices[position] = indices[position - 1];
                    position--;
                }
                indices[position] = value;
            }
            return;
        }
        for (int root = indices.Length / 2 - 1; root >= 0; root--)
            SiftDownPreparedIndices(indices, root, indices.Length);
        for (int end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftDownPreparedIndices(indices, 0, end);
        }
    }

    private void SiftDownPreparedIndices(Span<int> indices, int root, int count)
    {
        while (true)
        {
            int child = checked(root * 2 + 1);
            if (child >= count) return;
            if (child + 1 < count && ComparePreparedIndex(
                    indices[child], indices[child + 1]) < 0)
                child++;
            if (ComparePreparedIndex(indices[root], indices[child]) >= 0) return;
            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private int ComparePreparedIndex(int left, int right) =>
        _queryLatest[left].Id.CompareTo(_queryLatest[right].Id);

    private ChunkPublicationReadStatus PreparedRebase(
        SpatialChunkKey key,
        out ChunkPublicationBatch batch)
    {
        SaturatingIncrement(ref _preparedRebaseRequirements);
        _snapshotFallbacks++;
        batch = new ChunkPublicationBatch(
            ChunkPublicationReadStatus.SnapshotRequired, key, default, default, 0, 0);
        return ChunkPublicationReadStatus.SnapshotRequired;
    }

    private ChunkPublicationReadStatus PreparedRebase(
        SpatialChunkKey key,
        in ChunkRecord chunk,
        out ChunkPublicationBatch batch)
    {
        SaturatingIncrement(ref _preparedRebaseRequirements);
        _snapshotFallbacks++;
        batch = CreateBatch(ChunkPublicationReadStatus.SnapshotRequired, key, chunk, 0, 0);
        return ChunkPublicationReadStatus.SnapshotRequired;
    }

    private void RemovePreparedRecord(int historyNode, bool pressureEviction)
    {
        if (_preparedReadModel != PreparedChunkPublicationReadModel.PreparedLatestState) return;
        int publicationSlot = _preparedRecordPublicationSlots[historyNode];
        if (publicationSlot < 0) return;
        ref PreparedPublication publication = ref _preparedPublications[publicationSlot];
        if (!publication.Active || publication.RemainingRecords <= 0)
            throw new InvalidOperationException("Prepared publication retention accounting diverged.");
        if (_preparedRecords[historyNode].Operation == WorldPublicOperation.Remove)
        {
            publication.RemainingTombstones--;
            _retainedPreparedTombstones--;
        }
        _preparedRecords[historyNode] = default;
        _preparedRecordPublicationSlots[historyNode] = -1;
        publication.RemainingRecords--;
        _retainedPreparedRecords--;
        if (publication.RemainingRecords != 0) return;

        RemovePreparedLookup(publication.Key, publication.FromRevision, publicationSlot);
        publication = default;
        _freePreparedPublications[_freePreparedPublicationCount++] = publicationSlot;
        _retainedPreparedPublications--;
        SaturatingIncrement(ref _preparedRetentionEvictions);
        if (pressureEviction) SaturatingIncrement(ref _preparedPressureEvictions);
    }

    private void ClearPreparedPublications()
    {
        if (_preparedReadModel != PreparedChunkPublicationReadModel.PreparedLatestState) return;
        SaturatingAdd(ref _preparedRetentionEvictions, checked((ulong)_retainedPreparedPublications));
        Array.Clear(_preparedPublications);
        Array.Clear(_preparedRecords);
        Array.Clear(_preparedLookupKeys);
        Array.Clear(_preparedLookupValues);
        Array.Clear(_preparedLookupStates);
        Array.Fill(_preparedRecordPublicationSlots, -1);
        _freePreparedPublicationCount = _freePreparedPublications.Length;
        for (int index = 0; index < _freePreparedPublications.Length; index++)
            _freePreparedPublications[index] = _freePreparedPublications.Length - 1 - index;
        _retainedPreparedPublications = 0;
        _retainedPreparedRecords = 0;
        _retainedPreparedTombstones = 0;
    }

    private int FindPreparedPublication(SpatialChunkKey key, ulong fromRevision)
    {
        int bucket = PreparedHash(key, fromRevision) & _preparedLookupMask;
        for (int probe = 0; probe < _preparedLookupStates.Length; probe++)
        {
            byte state = _preparedLookupStates[bucket];
            if (state == 0) return -1;
            if (state == 1 && _preparedLookupKeys[bucket].Key == key &&
                _preparedLookupKeys[bucket].FromRevision == fromRevision)
                return _preparedLookupValues[bucket];
            bucket = (bucket + 1) & _preparedLookupMask;
        }
        return -1;
    }

    private void AddPreparedLookup(SpatialChunkKey key, ulong fromRevision, int publicationSlot)
    {
        int bucket = PreparedHash(key, fromRevision) & _preparedLookupMask;
        for (int probe = 0; probe < _preparedLookupStates.Length; probe++)
        {
            byte state = _preparedLookupStates[bucket];
            if (state == 0)
            {
                WritePreparedLookup(bucket, key, fromRevision, publicationSlot);
                return;
            }
            if (_preparedLookupKeys[bucket].Key == key &&
                _preparedLookupKeys[bucket].FromRevision == fromRevision)
                throw new InvalidOperationException("Prepared publication source interval was duplicated.");
            bucket = (bucket + 1) & _preparedLookupMask;
        }
        throw new InvalidOperationException("Prepared publication lookup exceeded its fixed bound.");
    }

    private void RemovePreparedLookup(SpatialChunkKey key, ulong fromRevision, int publicationSlot)
    {
        int bucket = PreparedHash(key, fromRevision) & _preparedLookupMask;
        for (int probe = 0; probe < _preparedLookupStates.Length; probe++)
        {
            byte state = _preparedLookupStates[bucket];
            if (state == 0) break;
            if (state == 1 && _preparedLookupKeys[bucket].Key == key &&
                _preparedLookupKeys[bucket].FromRevision == fromRevision)
            {
                if (_preparedLookupValues[bucket] != publicationSlot)
                    throw new InvalidOperationException("Prepared publication lookup identity diverged.");
                ClosePreparedLookupDeletionGap(bucket);
                return;
            }
            bucket = (bucket + 1) & _preparedLookupMask;
        }
        throw new InvalidOperationException("Prepared publication lookup removal lost its source interval.");
    }

    private void ClosePreparedLookupDeletionGap(int hole)
    {
        int scan = (hole + 1) & _preparedLookupMask;
        while (_preparedLookupStates[scan] != 0)
        {
            PreparedLookupKey key = _preparedLookupKeys[scan];
            int home = PreparedHash(key.Key, key.FromRevision) & _preparedLookupMask;
            if (((hole - home) & _preparedLookupMask) <
                ((scan - home) & _preparedLookupMask))
            {
                _preparedLookupKeys[hole] = key;
                _preparedLookupValues[hole] = _preparedLookupValues[scan];
                _preparedLookupStates[hole] = 1;
                hole = scan;
            }
            scan = (scan + 1) & _preparedLookupMask;
        }
        _preparedLookupKeys[hole] = default;
        _preparedLookupValues[hole] = 0;
        _preparedLookupStates[hole] = 0;
    }

    private void WritePreparedLookup(
        int bucket,
        SpatialChunkKey key,
        ulong fromRevision,
        int publicationSlot)
    {
        _preparedLookupKeys[bucket] = new PreparedLookupKey(key, fromRevision);
        _preparedLookupValues[bucket] = publicationSlot;
        _preparedLookupStates[bucket] = 1;
    }

    private static int PreparedHash(SpatialChunkKey key, ulong fromRevision)
    {
        unchecked
        {
            uint mixed = (uint)key.GetHashCode() ^
                (uint)fromRevision ^ (uint)(fromRevision >> 32);
            mixed ^= mixed >> 16;
            mixed *= 0x7feb352dU;
            mixed ^= mixed >> 15;
            return (int)mixed;
        }
    }

    private static void SaturatingAdd(ref ulong value, ulong amount)
    {
        value = ulong.MaxValue - value < amount ? ulong.MaxValue : value + amount;
    }

    private long CalculatePreparedPublicationReservedBytes() => checked(
        (long)_preparedPublications.Length * Unsafe.SizeOf<PreparedPublication>() +
        (long)_freePreparedPublications.Length * sizeof(int) +
        (long)_preparedRecords.Length * Unsafe.SizeOf<WorldPublicDeltaRecord>() +
        (long)_preparedRecordPublicationSlots.Length * sizeof(int) +
        (long)_preparedQueryNodes.Length * sizeof(int) +
        (long)_preparedLookupKeys.Length * Unsafe.SizeOf<PreparedLookupKey>() +
        (long)_preparedLookupValues.Length * sizeof(int) +
        _preparedLookupStates.Length);

    private readonly record struct PreparedLookupKey(
        SpatialChunkKey Key,
        ulong FromRevision);

    private struct PreparedPublication
    {
        internal SpatialChunkKey Key;
        internal int ChunkSlot;
        internal ulong FromRevision;
        internal ulong ToRevision;
        internal int FirstRecordNode;
        internal int LastRecordNode;
        internal int ExpectedRecords;
        internal int WrittenRecords;
        internal int RemainingRecords;
        internal int RemainingTombstones;
        internal bool Active;
    }
}
