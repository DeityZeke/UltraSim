using System.Buffers;
using System.Collections.Concurrent;

namespace UltraSim.ECS;

public sealed partial class World
{
    public sealed class PatchChunkPlanCache<TWrite>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        internal EcsSchema? Schema;
        internal int Version = -1;
        internal ComponentId WriteId;
        internal QueryPlan1[] Plans = Array.Empty<QueryPlan1>();
    }

    public sealed class PatchChunkPlanCache<TRead, TWrite>
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        internal EcsSchema? Schema;
        internal int Version = -1;
        internal ComponentId ReadId;
        internal ComponentId WriteId;
        internal QueryPlan2[] Plans = Array.Empty<QueryPlan2>();
    }

    public sealed class PatchChunkPlanCache<TRead1, TRead2, TWrite>
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        internal EcsSchema? Schema;
        internal int Version = -1;
        internal ComponentId ReadId1;
        internal ComponentId ReadId2;
        internal ComponentId WriteId;
        internal QueryPlan3[] Plans = Array.Empty<QueryPlan3>();
    }

    private readonly object _queryCacheSync = new();
    private ConcurrentDictionary<int, QueryPlan1[]> _queryCache1 = new();
    private ConcurrentDictionary<QueryKey2, QueryPlan2[]> _queryCache2 = new();
    private ConcurrentDictionary<QueryKey3, QueryPlan3[]> _queryCache3 = new();
    private ConcurrentDictionary<ArchetypeSignature, Archetype[]> _queryCacheN = new();
    private int _queryCacheVersion;

    private QueryPlan1[] GetCachedQueryPlan(ComponentId id)
    {
        EnsureQueryCachesFresh();

        if (_queryCache1.TryGetValue(id.Value, out QueryPlan1[]? matches))
            return matches;

        matches = BuildQueryPlan(id);
        return _queryCache1.GetOrAdd(id.Value, matches);
    }

    private QueryPlan2[] GetCachedQueryPlan(ComponentId id1, ComponentId id2)
    {
        EnsureQueryCachesFresh();

        var key = new QueryKey2(id1, id2);
        if (_queryCache2.TryGetValue(key, out QueryPlan2[]? matches))
            return matches;

        matches = BuildQueryPlan(id1, id2);
        return _queryCache2.GetOrAdd(key, matches);
    }

    private QueryPlan3[] GetCachedQueryPlan(ComponentId id1, ComponentId id2, ComponentId id3)
    {
        EnsureQueryCachesFresh();

        var key = new QueryKey3(id1, id2, id3);
        if (_queryCache3.TryGetValue(key, out QueryPlan3[]? matches))
            return matches;

        matches = BuildQueryPlan(id1, id2, id3);
        return _queryCache3.GetOrAdd(key, matches);
    }

    private Archetype[] GetCachedQueryArchetypes(ReadOnlySpan<ComponentId> required)
    {
        EnsureQueryCachesFresh();

        ArchetypeSignature signature = BuildQuerySignature(required);
        if (_queryCacheN.TryGetValue(signature, out Archetype[]? matches))
            return matches;

        matches = BuildMatchingArchetypes(required);
        return _queryCacheN.GetOrAdd(signature, matches);
    }

    private void EnsureQueryCachesFresh()
    {
        int currentVersion = Archetypes.Version;
        if (currentVersion == _queryCacheVersion)
            return;

        lock (_queryCacheSync)
        {
            currentVersion = Archetypes.Version;
            if (currentVersion == _queryCacheVersion)
                return;

            _queryCache1 = new ConcurrentDictionary<int, QueryPlan1[]>();
            _queryCache2 = new ConcurrentDictionary<QueryKey2, QueryPlan2[]>();
            _queryCache3 = new ConcurrentDictionary<QueryKey3, QueryPlan3[]>();
            _queryCacheN = new ConcurrentDictionary<ArchetypeSignature, Archetype[]>();
            _queryCacheVersion = currentVersion;
        }
    }

    private ReadOnlySpan<QueryPlan1> GetCachedQueryPlan<TWrite>(PatchChunkPlanCache<TWrite> cache)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(cache);
        EnsureQueryCachesFresh();

        if (!ReferenceEquals(cache.Schema, Schema))
        {
            cache.Schema = Schema;
            cache.WriteId = Schema.GetComponentId<TWrite>();
            cache.Version = -1;
            cache.Plans = Array.Empty<QueryPlan1>();
        }

        if (cache.Version != _queryCacheVersion)
        {
            cache.Plans = GetCachedQueryPlan(cache.WriteId);
            cache.Version = _queryCacheVersion;
        }

        return cache.Plans;
    }

    private ReadOnlySpan<QueryPlan2> GetCachedQueryPlan<TRead, TWrite>(PatchChunkPlanCache<TRead, TWrite> cache)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(cache);
        EnsureQueryCachesFresh();

        if (!ReferenceEquals(cache.Schema, Schema))
        {
            cache.Schema = Schema;
            cache.ReadId = Schema.GetComponentId<TRead>();
            cache.WriteId = Schema.GetComponentId<TWrite>();
            cache.Version = -1;
            cache.Plans = Array.Empty<QueryPlan2>();
        }

        if (cache.Version != _queryCacheVersion)
        {
            cache.Plans = GetCachedQueryPlan(cache.ReadId, cache.WriteId);
            cache.Version = _queryCacheVersion;
        }

        return cache.Plans;
    }

    private ReadOnlySpan<QueryPlan3> GetCachedQueryPlan<TRead1, TRead2, TWrite>(PatchChunkPlanCache<TRead1, TRead2, TWrite> cache)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(cache);
        EnsureQueryCachesFresh();

        if (!ReferenceEquals(cache.Schema, Schema))
        {
            cache.Schema = Schema;
            cache.ReadId1 = Schema.GetComponentId<TRead1>();
            cache.ReadId2 = Schema.GetComponentId<TRead2>();
            cache.WriteId = Schema.GetComponentId<TWrite>();
            cache.Version = -1;
            cache.Plans = Array.Empty<QueryPlan3>();
        }

        if (cache.Version != _queryCacheVersion)
        {
            cache.Plans = GetCachedQueryPlan(cache.ReadId1, cache.ReadId2, cache.WriteId);
            cache.Version = _queryCacheVersion;
        }

        return cache.Plans;
    }

    private QueryPlan1[] BuildQueryPlan(ComponentId id)
    {
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        var matches = new QueryPlan1[archetypes.Length];
        int count = 0;

        for (int i = 0; i < archetypes.Length; i++)
        {
            Archetype archetype = archetypes[i];
            int column1 = archetype.GetColumnIndex(id);
            if (column1 < 0)
                continue;

            matches[count++] = new QueryPlan1(archetype, column1);
        }

        if (count != matches.Length)
            Array.Resize(ref matches, count);

        return matches;
    }

    private QueryPlan2[] BuildQueryPlan(ComponentId id1, ComponentId id2)
    {
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        var matches = new QueryPlan2[archetypes.Length];
        int count = 0;

        for (int i = 0; i < archetypes.Length; i++)
        {
            Archetype archetype = archetypes[i];
            int column1 = archetype.GetColumnIndex(id1);
            if (column1 < 0)
                continue;

            int column2 = archetype.GetColumnIndex(id2);
            if (column2 < 0)
                continue;

            matches[count++] = new QueryPlan2(archetype, column1, column2);
        }

        if (count != matches.Length)
            Array.Resize(ref matches, count);

        return matches;
    }

    private QueryPlan3[] BuildQueryPlan(ComponentId id1, ComponentId id2, ComponentId id3)
    {
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        var matches = new QueryPlan3[archetypes.Length];
        int count = 0;

        for (int i = 0; i < archetypes.Length; i++)
        {
            Archetype archetype = archetypes[i];
            int column1 = archetype.GetColumnIndex(id1);
            if (column1 < 0)
                continue;

            int column2 = archetype.GetColumnIndex(id2);
            if (column2 < 0)
                continue;

            int column3 = archetype.GetColumnIndex(id3);
            if (column3 < 0)
                continue;

            matches[count++] = new QueryPlan3(archetype, column1, column2, column3);
        }

        if (count != matches.Length)
            Array.Resize(ref matches, count);

        return matches;
    }

    private Archetype[] BuildMatchingArchetypes(ReadOnlySpan<ComponentId> required)
    {
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        var matches = new Archetype[archetypes.Length];
        int count = 0;

        for (int i = 0; i < archetypes.Length; i++)
        {
            Archetype archetype = archetypes[i];
            if (!archetype.Signature.HasAll(required))
                continue;

            matches[count++] = archetype;
        }

        if (count != matches.Length)
            Array.Resize(ref matches, count);

        return matches;
    }

    private static ArchetypeSignature BuildQuerySignature(ReadOnlySpan<ComponentId> required)
    {
        if (required.Length <= 1)
            return new ArchetypeSignature(required);

        const int stackThreshold = 16;
        if (required.Length <= stackThreshold)
        {
            Span<ComponentId> buffer = stackalloc ComponentId[stackThreshold];
            required.CopyTo(buffer);
            int count = NormalizeQueryIds(buffer[..required.Length]);
            return new ArchetypeSignature(buffer[..count]);
        }

        ComponentId[] rented = ArrayPool<ComponentId>.Shared.Rent(required.Length);
        try
        {
            required.CopyTo(rented);
            int count = NormalizeQueryIds(rented.AsSpan(0, required.Length));
            return new ArchetypeSignature(rented.AsSpan(0, count));
        }
        finally
        {
            ArrayPool<ComponentId>.Shared.Return(rented, clearArray: false);
        }
    }

    private static int NormalizeQueryIds(Span<ComponentId> ids)
    {
        MemoryExtensions.Sort(ids, static (left, right) => left.Value.CompareTo(right.Value));

        int write = 0;
        int previous = int.MinValue;
        for (int i = 0; i < ids.Length; i++)
        {
            int current = ids[i].Value;
            if (current < 0)
                throw new InvalidOperationException("ComponentId.Invalid is not allowed.");

            if (current == previous)
                continue;

            previous = current;
            ids[write++] = ids[i];
        }

        return write;
    }

    private readonly struct QueryKey2 : IEquatable<QueryKey2>
    {
        public QueryKey2(ComponentId id1, ComponentId id2)
        {
            First = id1.Value;
            Second = id2.Value;
        }

        public int First { get; }
        public int Second { get; }

        public bool Equals(QueryKey2 other) => First == other.First && Second == other.Second;
        public override bool Equals(object? obj) => obj is QueryKey2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(First, Second);
    }

    internal readonly struct QueryPlan1
    {
        public QueryPlan1(Archetype archetype, int column1)
        {
            Archetype = archetype;
            Column1 = column1;
        }

        public Archetype Archetype { get; }
        public int Column1 { get; }
    }

    internal readonly struct QueryPlan2
    {
        public QueryPlan2(Archetype archetype, int column1, int column2)
        {
            Archetype = archetype;
            Column1 = column1;
            Column2 = column2;
        }

        public Archetype Archetype { get; }
        public int Column1 { get; }
        public int Column2 { get; }
    }

    internal readonly struct QueryPlan3
    {
        public QueryPlan3(Archetype archetype, int column1, int column2, int column3)
        {
            Archetype = archetype;
            Column1 = column1;
            Column2 = column2;
            Column3 = column3;
        }

        public Archetype Archetype { get; }
        public int Column1 { get; }
        public int Column2 { get; }
        public int Column3 { get; }
    }

    private readonly struct QueryKey3 : IEquatable<QueryKey3>
    {
        public QueryKey3(ComponentId id1, ComponentId id2, ComponentId id3)
        {
            First = id1.Value;
            Second = id2.Value;
            Third = id3.Value;
        }

        public int First { get; }
        public int Second { get; }
        public int Third { get; }

        public bool Equals(QueryKey3 other)
            => First == other.First && Second == other.Second && Third == other.Third;

        public override bool Equals(object? obj) => obj is QueryKey3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(First, Second, Third);
    }
}
