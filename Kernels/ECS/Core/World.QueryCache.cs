using System.Buffers;
using System.Collections.Concurrent;

namespace UltraSim.ECS;

public sealed partial class World
{
    private readonly object _queryCacheSync = new();
    private ConcurrentDictionary<int, Archetype[]> _queryCache1 = new();
    private ConcurrentDictionary<QueryKey2, Archetype[]> _queryCache2 = new();
    private ConcurrentDictionary<QueryKey3, Archetype[]> _queryCache3 = new();
    private ConcurrentDictionary<ArchetypeSignature, Archetype[]> _queryCacheN = new();
    private int _queryCacheVersion;

    private Archetype[] GetCachedQueryArchetypes(ComponentId id)
    {
        EnsureQueryCachesFresh();

        if (_queryCache1.TryGetValue(id.Value, out Archetype[]? matches))
            return matches;

        matches = BuildMatchingArchetypes(id);
        return _queryCache1.GetOrAdd(id.Value, matches);
    }

    private Archetype[] GetCachedQueryArchetypes(ComponentId id1, ComponentId id2)
    {
        EnsureQueryCachesFresh();

        var key = new QueryKey2(id1, id2);
        if (_queryCache2.TryGetValue(key, out Archetype[]? matches))
            return matches;

        matches = BuildMatchingArchetypes(id1, id2);
        return _queryCache2.GetOrAdd(key, matches);
    }

    private Archetype[] GetCachedQueryArchetypes(ComponentId id1, ComponentId id2, ComponentId id3)
    {
        EnsureQueryCachesFresh();

        var key = new QueryKey3(id1, id2, id3);
        if (_queryCache3.TryGetValue(key, out Archetype[]? matches))
            return matches;

        matches = BuildMatchingArchetypes(id1, id2, id3);
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

            _queryCache1 = new ConcurrentDictionary<int, Archetype[]>();
            _queryCache2 = new ConcurrentDictionary<QueryKey2, Archetype[]>();
            _queryCache3 = new ConcurrentDictionary<QueryKey3, Archetype[]>();
            _queryCacheN = new ConcurrentDictionary<ArchetypeSignature, Archetype[]>();
            _queryCacheVersion = currentVersion;
        }
    }

    private Archetype[] BuildMatchingArchetypes(ComponentId id)
    {
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        var matches = new Archetype[archetypes.Length];
        int count = 0;

        for (int i = 0; i < archetypes.Length; i++)
        {
            Archetype archetype = archetypes[i];
            if (!archetype.HasComponent(id))
                continue;

            matches[count++] = archetype;
        }

        if (count != matches.Length)
            Array.Resize(ref matches, count);

        return matches;
    }

    private Archetype[] BuildMatchingArchetypes(ComponentId id1, ComponentId id2)
    {
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        var matches = new Archetype[archetypes.Length];
        int count = 0;

        for (int i = 0; i < archetypes.Length; i++)
        {
            Archetype archetype = archetypes[i];
            if (!archetype.HasComponent(id1) || !archetype.HasComponent(id2))
                continue;

            matches[count++] = archetype;
        }

        if (count != matches.Length)
            Array.Resize(ref matches, count);

        return matches;
    }

    private Archetype[] BuildMatchingArchetypes(ComponentId id1, ComponentId id2, ComponentId id3)
    {
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        var matches = new Archetype[archetypes.Length];
        int count = 0;

        for (int i = 0; i < archetypes.Length; i++)
        {
            Archetype archetype = archetypes[i];
            if (!archetype.HasComponent(id1) || !archetype.HasComponent(id2) || !archetype.HasComponent(id3))
                continue;

            matches[count++] = archetype;
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
            int first = id1.Value;
            int second = id2.Value;

            if (second < first)
                (first, second) = (second, first);

            if (second == first)
                second = -1;

            First = first;
            Second = second;
        }

        public int First { get; }
        public int Second { get; }

        public bool Equals(QueryKey2 other) => First == other.First && Second == other.Second;
        public override bool Equals(object? obj) => obj is QueryKey2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(First, Second);
    }

    private readonly struct QueryKey3 : IEquatable<QueryKey3>
    {
        public QueryKey3(ComponentId id1, ComponentId id2, ComponentId id3)
        {
            int first = id1.Value;
            int second = id2.Value;
            int third = id3.Value;

            if (second < first)
                (first, second) = (second, first);
            if (third < second)
                (second, third) = (third, second);
            if (second < first)
                (first, second) = (second, first);

            if (second == first)
            {
                second = third;
                third = -1;
            }
            else if (third == second)
            {
                third = -1;
            }

            First = first;
            Second = second;
            Third = third;
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
