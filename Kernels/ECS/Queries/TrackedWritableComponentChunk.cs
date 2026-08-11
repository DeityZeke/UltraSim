using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Stack-only conditional-write view for one generated public chunk job.
/// Mutation is published before a mutable reference, dense span, or enable-bit
/// write becomes observable.
/// </summary>
public readonly ref struct TrackedWritableComponentChunk<T>
    where T : unmanaged, IComponent<T>
{
    private readonly EcsWorld<NativeChunkArena> _world;
    private readonly ArchetypeChunkStore<NativeChunkArena> _store;
    private readonly Span<T> _components;
    private readonly Span<ulong> _enableWords;
    private readonly uint _chunkIndex;
    private readonly int _columnIndex;
    private readonly int _rowOffset;
    private readonly bool _wholeChunk;
    private readonly ref ChangeVersion _invocationVersion;
    private readonly ref bool _chunkStamped;

    internal TrackedWritableComponentChunk(
        EcsWorld<NativeChunkArena> world,
        ArchetypeChunkStore<NativeChunkArena> store,
        uint chunkIndex,
        int columnIndex,
        int rowOffset,
        Span<T> components,
        Span<ulong> enableWords,
        bool wholeChunk,
        ref ChangeVersion invocationVersion,
        ref bool chunkStamped)
    {
        System.Diagnostics.Debug.Assert(world is not null);
        System.Diagnostics.Debug.Assert(store is not null);
        System.Diagnostics.Debug.Assert(rowOffset >= 0);
        _world = world;
        _store = store;
        _chunkIndex = chunkIndex;
        _columnIndex = columnIndex;
        _rowOffset = rowOffset;
        _components = components;
        _enableWords = enableWords;
        _wholeChunk = wholeChunk;
        _invocationVersion = ref invocationVersion;
        _chunkStamped = ref chunkStamped;
    }

    public int Count => _components.Length;

    public bool IsEnableable => !_enableWords.IsEmpty;

    /// <summary>
    /// Publishes the complete exposed range, then returns direct contiguous
    /// mutable access. Use this for dense writes.
    /// </summary>
    public Span<T> DenseSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            PublishRange(localRow: 0, Count, _wholeChunk);
            return _components;
        }
    }

    /// <summary>Publishes exactly one row, then returns its mutable component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetTracked(int row)
    {
        ValidateRow(row);
        PublishRange(row, rowCount: 1, wholeChunk: false);
        return ref _components[row];
    }

    /// <summary>Publishes and writes exactly one row.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTracked(int row, in T value)
    {
        ValidateRow(row);
        PublishRange(row, rowCount: 1, wholeChunk: false);
        _components[row] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(int row)
    {
        ValidateRow(row);
        int absoluteRow = _rowOffset + row;
        return _enableWords.IsEmpty ||
            (_enableWords[absoluteRow >> 6] & (1UL << (absoluteRow & 63))) != 0;
    }

    /// <summary>Publishes exactly one row before changing its enable state.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetEnabledTracked(int row, bool enabled)
    {
        if (_enableWords.IsEmpty)
            throw new InvalidOperationException("The writable component is not enableable.");
        ValidateRow(row);
        PublishRange(row, rowCount: 1, wholeChunk: false);
        int absoluteRow = _rowOffset + row;
        ref ulong word = ref _enableWords[absoluteRow >> 6];
        ulong mask = 1UL << (absoluteRow & 63);
        word = enabled ? word | mask : word & ~mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PublishRange(int localRow, int rowCount, bool wholeChunk)
    {
        ChangeVersion version = _world.AcquireTrackedJobChangeVersion(ref _invocationVersion);
        _store.StampTrackedJobRange(
            _chunkIndex,
            _columnIndex,
            _rowOffset + localRow,
            rowCount,
            version,
            wholeChunk,
            ref _chunkStamped);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateRow(int row)
    {
        if (unchecked((uint)row) >= unchecked((uint)Count))
            throw new ArgumentOutOfRangeException(nameof(row));
    }
}
