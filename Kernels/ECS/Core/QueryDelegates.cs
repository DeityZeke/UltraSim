namespace UltraSim.ECS;

public delegate void ReadOnlyQueryAction<T>(Entity entity, in T component)
    where T : unmanaged, IComponent<T>;

public delegate void ReadOnlyQueryAction<T1, T2>(Entity entity, in T1 component1, in T2 component2)
    where T1 : unmanaged, IComponent<T1>
    where T2 : unmanaged, IComponent<T2>;

public delegate void ReadOnlyQueryAction<T1, T2, T3>(Entity entity, in T1 component1, in T2 component2, in T3 component3)
    where T1 : unmanaged, IComponent<T1>
    where T2 : unmanaged, IComponent<T2>
    where T3 : unmanaged, IComponent<T3>;

public delegate void ReadOnlyChunkAction<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> component1)
    where T : unmanaged, IComponent<T>;

public delegate void ReadOnlyChunkAction<T1, T2>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T1> component1, ReadOnlySpan<T2> component2)
    where T1 : unmanaged, IComponent<T1>
    where T2 : unmanaged, IComponent<T2>;

public delegate void ReadOnlyChunkAction<T1, T2, T3>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T1> component1, ReadOnlySpan<T2> component2, ReadOnlySpan<T3> component3)
    where T1 : unmanaged, IComponent<T1>
    where T2 : unmanaged, IComponent<T2>
    where T3 : unmanaged, IComponent<T3>;

public delegate void PatchQueryAction<TRead, TWrite>(Entity entity, in TRead read, ref TWrite write)
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>;

public delegate void PatchQueryAction<TRead1, TRead2, TWrite>(Entity entity, in TRead1 read1, in TRead2 read2, ref TWrite write)
    where TRead1 : unmanaged, IComponent<TRead1>
    where TRead2 : unmanaged, IComponent<TRead2>
    where TWrite : unmanaged, IComponent<TWrite>;

public delegate void PatchChunkAction<TWrite>(ReadOnlySpan<Entity> entities, Span<TWrite> writes)
    where TWrite : unmanaged, IComponent<TWrite>;

public delegate void PatchChunkAction<TRead, TWrite>(ReadOnlySpan<Entity> entities, ReadOnlySpan<TRead> reads, Span<TWrite> writes)
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>;

public delegate void PatchChunkAction<TRead1, TRead2, TWrite>(ReadOnlySpan<Entity> entities, ReadOnlySpan<TRead1> reads1, ReadOnlySpan<TRead2> reads2, Span<TWrite> writes)
    where TRead1 : unmanaged, IComponent<TRead1>
    where TRead2 : unmanaged, IComponent<TRead2>
    where TWrite : unmanaged, IComponent<TWrite>;

public delegate int SparsePatchChunkAction<TWrite>(ReadOnlySpan<Entity> entities, Span<TWrite> writes, Span<int> dirtyIndices)
    where TWrite : unmanaged, IComponent<TWrite>;

public delegate int SparsePatchChunkAction<TRead, TWrite>(ReadOnlySpan<Entity> entities, ReadOnlySpan<TRead> reads, Span<TWrite> writes, Span<int> dirtyIndices)
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>;

public delegate int SparsePatchChunkAction<TRead1, TRead2, TWrite>(ReadOnlySpan<Entity> entities, ReadOnlySpan<TRead1> reads1, ReadOnlySpan<TRead2> reads2, Span<TWrite> writes, Span<int> dirtyIndices)
    where TRead1 : unmanaged, IComponent<TRead1>
    where TRead2 : unmanaged, IComponent<TRead2>
    where TWrite : unmanaged, IComponent<TWrite>;
