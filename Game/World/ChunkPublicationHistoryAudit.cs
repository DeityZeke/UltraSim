using System.Diagnostics;
using System.Runtime.CompilerServices;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Cold diagnostic capacity for the PF5-R1 retained-history audit.</summary>
public readonly record struct ChunkPublicationHistoryAuditLimits(int MaximumDistinctRanges)
{
    public bool Enabled => MaximumDistinctRanges > 0;

    internal void Validate()
    {
        if (MaximumDistinctRanges is < 16 or > 262_144 ||
            (MaximumDistinctRanges & (MaximumDistinctRanges - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumDistinctRanges),
                "The history-audit range capacity must be a power of two in [16, 262144].");
    }
}

public readonly record struct ChunkPublicationHistoryRangeKey(
    PublicationEpoch SourceEpoch,
    SpatialChunkKey ChunkKey,
    ulong FromRevision,
    ulong ToRevision);

public readonly record struct ChunkPublicationHistoryRangeAudit(
    ChunkPublicationHistoryRangeKey Range,
    ulong Requests,
    int RecordCount,
    ulong CanonicalContentHash,
    int MaterializedRecordStorageBytes,
    ulong FirstPreparationTicks,
    ulong TotalPreparationTicks,
    ulong HistoryNodesExamined,
    ulong HistoryNodesSkipped,
    ulong HistoryNodesAfterCursor,
    ulong DuplicateMutations,
    ulong DuplicateMutationsCollapsed,
    ulong CurrentRecordLookups,
    bool ContentMismatch);

public readonly record struct ChunkPublicationHistoryRetentionAudit(
    SpatialChunkKey ChunkKey,
    ulong CurrentRevision,
    ulong HistoryFloor,
    int RetainedEntries,
    ulong OldestRetainedRevision,
    ulong NewestRetainedRevision,
    ulong OldestAuditedCommit,
    ulong NewestAuditedCommit,
    long OldestAuditedAgeStopwatchTicks);

/// <summary>
/// Cold immutable diagnostic copy. Arrays are detached from production state; mutating a returned
/// array cannot influence publication, delivery, or proof behavior.
/// </summary>
public sealed record ChunkPublicationHistoryAuditSnapshot(
    bool Enabled,
    PublicationEpoch SourceEpoch,
    ulong DeltaRequests,
    ulong DeltaRecordsEmitted,
    ulong HistoryNodesExamined,
    ulong HistoryNodesSkipped,
    ulong HistoryNodesAfterCursor,
    ulong DuplicateMutations,
    ulong DuplicateMutationsCollapsed,
    ulong UniqueEntitiesSurvivingCollapse,
    ulong CurrentRecordLookups,
    ulong RecordsSorted,
    ulong HistoryWalkTicks,
    ulong CollapseTicks,
    ulong CurrentStateLookupTicks,
    ulong SortTicks,
    ulong CompletePreparationTicks,
    ulong UniqueRangePreparationTicks,
    ulong ShadowReusablePreparationTicks,
    ulong ShadowContentMismatches,
    int DistinctRanges,
    ulong RepeatedRangeRequests,
    bool RangeTableOverflowed,
    ulong SnapshotReads,
    ulong SnapshotRecords,
    ulong SnapshotCopyAndSortTicks,
    ulong Commits,
    ulong DirtyRecordsProduced,
    ulong DirtyChunksProduced,
    ulong HistoryEntriesProduced,
    int HistoryOccupancy,
    int HistoryOccupancyHighWater,
    ulong PressureEvictions,
    ulong NonPressureRemovals,
    ulong HistoryFloorAdvances,
    ulong GlobalHistoryInvalidations,
    ulong GloballyInvalidatedEntries,
    ulong[] HistoryNodesExaminedHistogram,
    ulong[] HistoryNodesAfterCursorHistogram,
    ulong[] DuplicateMutationHistogram,
    ulong[] RecordsEmittedHistogram,
    ulong[] RangeLengthHistogram,
    ChunkPublicationHistoryRangeAudit[] Ranges,
    ChunkPublicationHistoryRetentionAudit[] Retention)
{
    public static ChunkPublicationHistoryAuditSnapshot Disabled(PublicationEpoch epoch) => new(
        Enabled: false,
        SourceEpoch: epoch,
        DeltaRequests: 0,
        DeltaRecordsEmitted: 0,
        HistoryNodesExamined: 0,
        HistoryNodesSkipped: 0,
        HistoryNodesAfterCursor: 0,
        DuplicateMutations: 0,
        DuplicateMutationsCollapsed: 0,
        UniqueEntitiesSurvivingCollapse: 0,
        CurrentRecordLookups: 0,
        RecordsSorted: 0,
        HistoryWalkTicks: 0,
        CollapseTicks: 0,
        CurrentStateLookupTicks: 0,
        SortTicks: 0,
        CompletePreparationTicks: 0,
        UniqueRangePreparationTicks: 0,
        ShadowReusablePreparationTicks: 0,
        ShadowContentMismatches: 0,
        DistinctRanges: 0,
        RepeatedRangeRequests: 0,
        RangeTableOverflowed: false,
        SnapshotReads: 0,
        SnapshotRecords: 0,
        SnapshotCopyAndSortTicks: 0,
        Commits: 0,
        DirtyRecordsProduced: 0,
        DirtyChunksProduced: 0,
        HistoryEntriesProduced: 0,
        HistoryOccupancy: 0,
        HistoryOccupancyHighWater: 0,
        PressureEvictions: 0,
        NonPressureRemovals: 0,
        HistoryFloorAdvances: 0,
        GlobalHistoryInvalidations: 0,
        GloballyInvalidatedEntries: 0,
        HistoryNodesExaminedHistogram: [],
        HistoryNodesAfterCursorHistogram: [],
        DuplicateMutationHistogram: [],
        RecordsEmittedHistogram: [],
        RangeLengthHistogram: [],
        Ranges: [],
        Retention: []);
}

internal sealed class ChunkPublicationHistoryAuditState
{
    internal static readonly ulong[] HistogramUpperBounds =
        [0, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1_024, 2_048, 4_096, 8_192, ulong.MaxValue];

    private readonly RangeSlot[] _ranges;
    private readonly int _rangeMask;
    private readonly ulong[] _historyCommitSequences;
    private readonly long[] _historyTimestamps;
    private readonly ulong[] _historyNodesExaminedHistogram = new ulong[HistogramUpperBounds.Length];
    private readonly ulong[] _historyNodesAfterCursorHistogram = new ulong[HistogramUpperBounds.Length];
    private readonly ulong[] _duplicateMutationHistogram = new ulong[HistogramUpperBounds.Length];
    private readonly ulong[] _recordsEmittedHistogram = new ulong[HistogramUpperBounds.Length];
    private readonly ulong[] _rangeLengthHistogram = new ulong[HistogramUpperBounds.Length];
    private ulong _deltaRequests;
    private ulong _deltaRecordsEmitted;
    private ulong _historyNodesExamined;
    private ulong _historyNodesSkipped;
    private ulong _historyNodesAfterCursor;
    private ulong _duplicateMutations;
    private ulong _duplicateMutationsCollapsed;
    private ulong _uniqueEntitiesSurvivingCollapse;
    private ulong _currentRecordLookups;
    private ulong _recordsSorted;
    private ulong _historyWalkTicks;
    private ulong _collapseTicks;
    private ulong _currentStateLookupTicks;
    private ulong _sortTicks;
    private ulong _completePreparationTicks;
    private ulong _uniqueRangePreparationTicks;
    private ulong _shadowContentMismatches;
    private int _distinctRanges;
    private ulong _repeatedRangeRequests;
    private bool _rangeTableOverflowed;
    private ulong _snapshotReads;
    private ulong _snapshotRecords;
    private ulong _snapshotCopyAndSortTicks;
    private ulong _commits;
    private ulong _dirtyRecordsProduced;
    private ulong _dirtyChunksProduced;
    private ulong _historyEntriesProduced;
    private int _historyOccupancy;
    private int _historyOccupancyHighWater;
    private ulong _pressureEvictions;
    private ulong _nonPressureRemovals;
    private ulong _historyFloorAdvances;
    private ulong _globalHistoryInvalidations;
    private ulong _globallyInvalidatedEntries;

    internal ChunkPublicationHistoryAuditState(
        ChunkPublicationHistoryAuditLimits limits,
        int historyCapacity,
        int chunkCapacity)
    {
        _ranges = new RangeSlot[limits.MaximumDistinctRanges];
        _rangeMask = _ranges.Length - 1;
        _historyCommitSequences = new ulong[historyCapacity];
        _historyTimestamps = new long[historyCapacity];
        _ = chunkCapacity; // Retention is captured from the source's bounded chunk table.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal long Timestamp() => Stopwatch.GetTimestamp();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ulong Elapsed(long started)
    {
        long elapsed = Stopwatch.GetTimestamp() - started;
        return elapsed > 0 ? checked((ulong)elapsed) : 0;
    }

    internal void RecordCommit(ulong commit, int dirtyRecords, int affectedChunks)
    {
        _ = commit;
        Add(ref _commits, 1);
        Add(ref _dirtyRecordsProduced, checked((ulong)dirtyRecords));
        Add(ref _dirtyChunksProduced, checked((ulong)affectedChunks));
    }

    internal void RecordHistoryEntry(int node, ulong commit, ulong revision)
    {
        _ = revision;
        _historyCommitSequences[node] = commit;
        _historyTimestamps[node] = Stopwatch.GetTimestamp();
        Add(ref _historyEntriesProduced, 1);
    }

    internal void RecordHistoryRemoval(int node, ulong revision, bool pressureEviction)
    {
        _ = revision;
        _historyCommitSequences[node] = 0;
        _historyTimestamps[node] = 0;
        if (pressureEviction) Add(ref _pressureEvictions, 1);
        else Add(ref _nonPressureRemovals, 1);
    }

    internal void RecordHistoryFloorAdvance(
        SpatialChunkKey key, ulong previous, ulong current, int removed)
    {
        _ = key;
        _ = removed;
        if (current > previous) Add(ref _historyFloorAdvances, 1);
    }

    internal void RecordGlobalInvalidation(int entries)
    {
        Add(ref _globalHistoryInvalidations, 1);
        Add(ref _globallyInvalidatedEntries, checked((ulong)entries));
        Array.Clear(_historyCommitSequences);
        Array.Clear(_historyTimestamps);
    }

    internal void ObserveOccupancy(int occupancy)
    {
        _historyOccupancy = occupancy;
        _historyOccupancyHighWater = Math.Max(_historyOccupancyHighWater, occupancy);
    }

    internal void RecordSnapshot(
        SpatialChunkKey key, ulong revision, int records, long started)
    {
        _ = key;
        _ = revision;
        Add(ref _snapshotReads, 1);
        Add(ref _snapshotRecords, checked((ulong)records));
        Add(ref _snapshotCopyAndSortTicks, Elapsed(started));
    }

    internal void RecordDelta(
        PublicationEpoch epoch,
        SpatialChunkKey key,
        ulong fromRevision,
        ulong toRevision,
        ReadOnlySpan<WorldPublicDeltaRecord> records,
        ulong nodesExamined,
        ulong nodesSkipped,
        ulong nodesAfterCursor,
        ulong duplicateMutations,
        ulong duplicateCollapses,
        ulong currentRecordLookups,
        ulong historyTicks,
        ulong collapseTicks,
        ulong lookupTicks,
        ulong sortTicks,
        ulong completeTicks)
    {
        ulong contentHash = Hash(records);
        int sourceBytes = checked(records.Length * Unsafe.SizeOf<WorldPublicDeltaRecord>());
        var range = new ChunkPublicationHistoryRangeKey(
            epoch, key, fromRevision, toRevision);
        int slot = FindRange(range, out bool found);
        if (slot >= 0)
        {
            ref RangeSlot retained = ref _ranges[slot];
            if (!found)
            {
                retained = new RangeSlot(range, contentHash, records.Length, sourceBytes,
                    completeTicks);
                _distinctRanges++;
                Add(ref _uniqueRangePreparationTicks, completeTicks);
            }
            else
            {
                Add(ref _repeatedRangeRequests, 1);
                if (retained.ContentHash != contentHash ||
                    retained.RecordCount != records.Length ||
                    retained.MaterializedRecordStorageBytes != sourceBytes)
                {
                    retained.ContentMismatch = true;
                    Add(ref _shadowContentMismatches, 1);
                }
            }
            Add(ref retained.Requests, 1);
            Add(ref retained.TotalPreparationTicks, completeTicks);
            Add(ref retained.HistoryNodesExamined, nodesExamined);
            Add(ref retained.HistoryNodesSkipped, nodesSkipped);
            Add(ref retained.HistoryNodesAfterCursor, nodesAfterCursor);
            Add(ref retained.DuplicateMutations, duplicateMutations);
            Add(ref retained.DuplicateMutationsCollapsed, duplicateCollapses);
            Add(ref retained.CurrentRecordLookups, currentRecordLookups);
        }
        else
        {
            _rangeTableOverflowed = true;
        }

        Add(ref _deltaRequests, 1);
        Add(ref _deltaRecordsEmitted, checked((ulong)records.Length));
        Add(ref _historyNodesExamined, nodesExamined);
        Add(ref _historyNodesSkipped, nodesSkipped);
        Add(ref _historyNodesAfterCursor, nodesAfterCursor);
        Add(ref _duplicateMutations, duplicateMutations);
        Add(ref _duplicateMutationsCollapsed, duplicateCollapses);
        Add(ref _uniqueEntitiesSurvivingCollapse, checked((ulong)records.Length));
        Add(ref _currentRecordLookups, currentRecordLookups);
        Add(ref _recordsSorted, checked((ulong)records.Length));
        Add(ref _historyWalkTicks, historyTicks);
        Add(ref _collapseTicks, collapseTicks);
        Add(ref _currentStateLookupTicks, lookupTicks);
        Add(ref _sortTicks, sortTicks);
        Add(ref _completePreparationTicks, completeTicks);
        RecordHistogram(_historyNodesExaminedHistogram, nodesExamined);
        RecordHistogram(_historyNodesAfterCursorHistogram, nodesAfterCursor);
        RecordHistogram(_duplicateMutationHistogram, duplicateMutations);
        RecordHistogram(_recordsEmittedHistogram, checked((ulong)records.Length));
        RecordHistogram(_rangeLengthHistogram, toRevision - fromRevision);
    }

    internal ChunkPublicationHistoryAuditSnapshot Capture(ChunkPublicationStore source)
    {
        var ranges = new ChunkPublicationHistoryRangeAudit[_distinctRanges];
        int rangeCount = 0;
        for (int index = 0; index < _ranges.Length; index++)
        {
            ref readonly RangeSlot slot = ref _ranges[index];
            if (!slot.Occupied) continue;
            ranges[rangeCount++] = slot.Capture();
        }
        Array.Sort(ranges, static (left, right) =>
        {
            int key = left.Range.ChunkKey.CompareTo(right.Range.ChunkKey);
            if (key != 0) return key;
            key = left.Range.FromRevision.CompareTo(right.Range.FromRevision);
            return key != 0 ? key : left.Range.ToRevision.CompareTo(right.Range.ToRevision);
        });
        ChunkPublicationHistoryRetentionAudit[] retention = source.CaptureHistoryRetention(
            _historyCommitSequences, _historyTimestamps);
        ulong reusableTicks = _completePreparationTicks >= _uniqueRangePreparationTicks
            ? _completePreparationTicks - _uniqueRangePreparationTicks
            : 0;
        return new ChunkPublicationHistoryAuditSnapshot(
            true, source.Epoch, _deltaRequests, _deltaRecordsEmitted,
            _historyNodesExamined, _historyNodesSkipped, _historyNodesAfterCursor,
            _duplicateMutations, _duplicateMutationsCollapsed,
            _uniqueEntitiesSurvivingCollapse, _currentRecordLookups, _recordsSorted,
            _historyWalkTicks, _collapseTicks, _currentStateLookupTicks, _sortTicks,
            _completePreparationTicks, _uniqueRangePreparationTicks, reusableTicks,
            _shadowContentMismatches, _distinctRanges, _repeatedRangeRequests,
            _rangeTableOverflowed, _snapshotReads, _snapshotRecords,
            _snapshotCopyAndSortTicks, _commits, _dirtyRecordsProduced,
            _dirtyChunksProduced, _historyEntriesProduced, _historyOccupancy,
            _historyOccupancyHighWater, _pressureEvictions, _nonPressureRemovals,
            _historyFloorAdvances, _globalHistoryInvalidations, _globallyInvalidatedEntries,
            (ulong[])_historyNodesExaminedHistogram.Clone(),
            (ulong[])_historyNodesAfterCursorHistogram.Clone(),
            (ulong[])_duplicateMutationHistogram.Clone(),
            (ulong[])_recordsEmittedHistogram.Clone(),
            (ulong[])_rangeLengthHistogram.Clone(), ranges, retention);
    }

    private int FindRange(in ChunkPublicationHistoryRangeKey range, out bool found)
    {
        int slot = Hash(range) & _rangeMask;
        for (int probe = 0; probe < _ranges.Length; probe++)
        {
            if (!_ranges[slot].Occupied)
            {
                found = false;
                return slot;
            }
            if (_ranges[slot].Range == range)
            {
                found = true;
                return slot;
            }
            slot = (slot + 1) & _rangeMask;
        }
        found = false;
        return -1;
    }

    private static int Hash(in ChunkPublicationHistoryRangeKey range)
    {
        ulong hash = 14_695_981_039_346_656_003UL;
        Mix(ref hash, range.SourceEpoch.Value);
        Mix(ref hash, range.ChunkKey.Space.Value);
        Mix(ref hash, unchecked((uint)range.ChunkKey.Coord.X));
        Mix(ref hash, unchecked((uint)range.ChunkKey.Coord.Z));
        Mix(ref hash, range.FromRevision);
        Mix(ref hash, range.ToRevision);
        return unchecked((int)(hash ^ (hash >> 32)));
    }

    private static ulong Hash(ReadOnlySpan<WorldPublicDeltaRecord> records)
    {
        ulong hash = 14_695_981_039_346_656_003UL;
        Mix(ref hash, checked((ulong)records.Length));
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly WorldPublicDeltaRecord value = ref records[index];
            Mix(ref hash, (byte)value.Operation);
            Mix(ref hash, (ushort)value.Fields);
            Mix(ref hash, value.Id.Index);
            Mix(ref hash, value.Id.Generation);
            Mix(ref hash, value.EntityPublicVersion);
            Mix(ref hash, value.Current.PrefabId);
            Mix(ref hash, unchecked((ulong)value.Current.Position.GlobalXMillimeters));
            Mix(ref hash, unchecked((ulong)value.Current.Position.YMillimeters));
            Mix(ref hash, unchecked((ulong)value.Current.Position.GlobalZMillimeters));
            Mix(ref hash, value.Current.Orientation.Units);
            Mix(ref hash, (ushort)value.Current.StateFlags);
            Mix(ref hash, value.Current.LastChangedRevision.Value);
        }
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Mix(ref ulong hash, ulong value)
    {
        unchecked
        {
            hash ^= value;
            hash *= 1_099_511_628_211UL;
        }
    }

    private static void RecordHistogram(ulong[] histogram, ulong value)
    {
        int index = 0;
        while (value > HistogramUpperBounds[index]) index++;
        Add(ref histogram[index], 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Add(ref ulong destination, ulong value) =>
        destination = ulong.MaxValue - destination < value ? ulong.MaxValue : destination + value;

    private struct RangeSlot
    {
        internal RangeSlot(
            ChunkPublicationHistoryRangeKey range,
            ulong contentHash,
            int recordCount,
            int sourceBytes,
            ulong firstTicks)
        {
            Range = range;
            ContentHash = contentHash;
            RecordCount = recordCount;
            MaterializedRecordStorageBytes = sourceBytes;
            FirstPreparationTicks = firstTicks;
            Occupied = true;
        }

        internal ChunkPublicationHistoryRangeKey Range;
        internal ulong Requests;
        internal int RecordCount;
        internal ulong ContentHash;
        internal int MaterializedRecordStorageBytes;
        internal ulong FirstPreparationTicks;
        internal ulong TotalPreparationTicks;
        internal ulong HistoryNodesExamined;
        internal ulong HistoryNodesSkipped;
        internal ulong HistoryNodesAfterCursor;
        internal ulong DuplicateMutations;
        internal ulong DuplicateMutationsCollapsed;
        internal ulong CurrentRecordLookups;
        internal bool ContentMismatch;
        internal bool Occupied;

        internal readonly ChunkPublicationHistoryRangeAudit Capture() => new(
            Range, Requests, RecordCount, ContentHash, MaterializedRecordStorageBytes,
            FirstPreparationTicks, TotalPreparationTicks, HistoryNodesExamined,
            HistoryNodesSkipped, HistoryNodesAfterCursor, DuplicateMutations,
            DuplicateMutationsCollapsed, CurrentRecordLookups, ContentMismatch);
    }
}

public sealed partial class ChunkPublicationStore
{
    internal ChunkPublicationHistoryRetentionAudit[] CaptureHistoryRetention(
        ReadOnlySpan<ulong> historyCommitSequences,
        ReadOnlySpan<long> historyTimestamps)
    {
        var retained = new ChunkPublicationHistoryRetentionAudit[_chunkCount];
        int written = 0;
        long now = Stopwatch.GetTimestamp();
        for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
        {
            ref readonly ChunkRecord chunk = ref _chunks[chunkIndex];
            if (!chunk.Occupied) continue;
            int count = 0;
            ulong oldestRevision = 0;
            ulong newestRevision = 0;
            ulong oldestCommit = 0;
            ulong newestCommit = 0;
            long oldestTimestamp = 0;
            for (int node = chunk.HistoryHead; node >= 0; node = _history[node].NextChunk)
            {
                ref readonly HistoryEntry entry = ref _history[node];
                ulong commit = historyCommitSequences[node];
                long timestamp = historyTimestamps[node];
                if (count == 0) oldestRevision = entry.Revision;
                if (oldestTimestamp == 0 && timestamp != 0)
                {
                    oldestCommit = commit;
                    oldestTimestamp = timestamp;
                }
                newestRevision = entry.Revision;
                if (timestamp != 0) newestCommit = commit;
                count++;
            }
            retained[written++] = new ChunkPublicationHistoryRetentionAudit(
                chunk.Key, chunk.Revision, chunk.HistoryFloor, count,
                oldestRevision, newestRevision, oldestCommit, newestCommit,
                oldestTimestamp == 0 ? 0 : Math.Max(0, now - oldestTimestamp));
        }
        if (written != retained.Length)
            throw new InvalidOperationException("Publication chunk retention census diverged.");
        Array.Sort(retained, static (left, right) => left.ChunkKey.CompareTo(right.ChunkKey));
        return retained;
    }
}
