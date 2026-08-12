using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Single-owner fixed ring retaining the latest lifecycle facts.</summary>
internal sealed class LifecycleHistory
{
    private readonly LifecycleRecord[] _records;
    private int _nextIndex;
    private int _retainedCount;
    private int _highWatermark;
    private ulong _nextSequence;
    private ulong _overwrittenCount;

    internal LifecycleHistory(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _records = new LifecycleRecord[capacity];
    }

    internal int Capacity => _records.Length;

    internal long ReservedBytes => checked((long)_records.Length * Unsafe.SizeOf<LifecycleRecord>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Publish(LifecycleKind kind, Entity entity, ComponentId componentId)
    {
        System.Diagnostics.Debug.Assert(entity.IsValid);
        System.Diagnostics.Debug.Assert(
            kind is LifecycleKind.ComponentAdded or LifecycleKind.ComponentRemoved
                ? componentId.IsValid
                : componentId == ComponentId.Invalid);

        _records[_nextIndex] = new LifecycleRecord(_nextSequence, kind, entity, componentId);
        _nextIndex++;
        if (_nextIndex == _records.Length)
            _nextIndex = 0;

        if (_retainedCount != _records.Length)
        {
            _retainedCount++;
            if (_retainedCount > _highWatermark)
                _highWatermark = _retainedCount;
        }
        else
        {
            _overwrittenCount = unchecked(_overwrittenCount + 1);
        }

        _nextSequence = unchecked(_nextSequence + 1);
    }

    internal LifecycleCursor CaptureFromBeginning(WorldId worldId) =>
        new(worldId, OldestRetainedSequence);

    internal LifecycleCursor CaptureFromNow(WorldId worldId) =>
        new(worldId, _nextSequence);

    internal LifecycleReadResult Read(ref LifecycleCursor cursor, Span<LifecycleRecord> destination)
    {
        ulong pending = unchecked(_nextSequence - cursor.NextSequence);
        ulong oldest = OldestRetainedSequence;
        if (pending == 0)
        {
            return new LifecycleReadResult(
                LifecycleReadStatus.NoData,
                0,
                0,
                oldest,
                _nextSequence);
        }

        if (pending > unchecked((ulong)_retainedCount))
        {
            return new LifecycleReadResult(
                LifecycleReadStatus.ResyncRequired,
                0,
                pending,
                oldest,
                _nextSequence);
        }

        int count = Math.Min(destination.Length, checked((int)pending));
        if (count != 0)
        {
            int oldestIndex = _nextIndex - _retainedCount;
            if (oldestIndex < 0)
                oldestIndex += _records.Length;
            int offset = checked((int)unchecked(cursor.NextSequence - oldest));
            int sourceIndex = oldestIndex + offset;
            if (sourceIndex >= _records.Length)
                sourceIndex -= _records.Length;

            int firstCount = Math.Min(count, _records.Length - sourceIndex);
            _records.AsSpan(sourceIndex, firstCount).CopyTo(destination);
            int secondCount = count - firstCount;
            if (secondCount != 0)
                _records.AsSpan(0, secondCount).CopyTo(destination[firstCount..]);
            cursor.Advance(count);
        }

        return new LifecycleReadResult(
            LifecycleReadStatus.Success,
            count,
            pending - unchecked((ulong)count),
            oldest,
            _nextSequence);
    }

    internal LifecycleHistorySnapshot GetSnapshot() => new(
        _records.Length,
        _retainedCount,
        _highWatermark,
        _overwrittenCount,
        OldestRetainedSequence,
        _nextSequence,
        ReservedBytes);

    internal void SetNextSequenceForTest(ulong sequence)
    {
        if (_retainedCount != 0)
            throw new InvalidOperationException("Sequence test seeding requires an empty lifecycle history.");
        _nextSequence = sequence;
    }

    private ulong OldestRetainedSequence =>
        unchecked(_nextSequence - unchecked((ulong)_retainedCount));
}
