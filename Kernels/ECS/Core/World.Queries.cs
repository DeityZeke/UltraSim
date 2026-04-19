using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public sealed partial class World
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArchetypeManager.ArchetypeEnumerator QueryArchetypes(ReadOnlySpan<ComponentId> required)
    {
        return Archetypes.EnumerateWith(required);
    }

    public int Query<T>(ReadOnlyQueryAction<T> action)
        where T : unmanaged, IComponent<T>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId id = Schema.GetComponentId<T>();
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < archetypes.Length; archetypeIndex++)
        {
            Archetype archetype = archetypes[archetypeIndex];
            if (!archetype.HasComponent(id))
                continue;

            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            ReadOnlySpan<T> values = archetype.GetComponentSpan<T>(id);
            for (int i = 0; i < entities.Length; i++)
            {
                action(entities[i], in values[i]);
                matched++;
            }
        }

        return matched;
    }

    public int Query<T1, T2>(ReadOnlyQueryAction<T1, T2> action)
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId id1 = Schema.GetComponentId<T1>();
        ComponentId id2 = Schema.GetComponentId<T2>();
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < archetypes.Length; archetypeIndex++)
        {
            Archetype archetype = archetypes[archetypeIndex];
            if (!archetype.HasComponent(id1) || !archetype.HasComponent(id2))
                continue;

            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            ReadOnlySpan<T1> values1 = archetype.GetComponentSpan<T1>(id1);
            ReadOnlySpan<T2> values2 = archetype.GetComponentSpan<T2>(id2);
            for (int i = 0; i < entities.Length; i++)
            {
                action(entities[i], in values1[i], in values2[i]);
                matched++;
            }
        }

        return matched;
    }

    public int Query<T1, T2, T3>(ReadOnlyQueryAction<T1, T2, T3> action)
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
        where T3 : unmanaged, IComponent<T3>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId id1 = Schema.GetComponentId<T1>();
        ComponentId id2 = Schema.GetComponentId<T2>();
        ComponentId id3 = Schema.GetComponentId<T3>();
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < archetypes.Length; archetypeIndex++)
        {
            Archetype archetype = archetypes[archetypeIndex];
            if (!archetype.HasComponent(id1) || !archetype.HasComponent(id2) || !archetype.HasComponent(id3))
                continue;

            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            ReadOnlySpan<T1> values1 = archetype.GetComponentSpan<T1>(id1);
            ReadOnlySpan<T2> values2 = archetype.GetComponentSpan<T2>(id2);
            ReadOnlySpan<T3> values3 = archetype.GetComponentSpan<T3>(id3);
            for (int i = 0; i < entities.Length; i++)
            {
                action(entities[i], in values1[i], in values2[i], in values3[i]);
                matched++;
            }
        }

        return matched;
    }

    public QueryChunkEnumerator<T> QueryChunks<T>()
        where T : unmanaged, IComponent<T>
    {
        return new QueryChunkEnumerator<T>(Archetypes.AsSpan(), Schema.GetComponentId<T>());
    }

    public QueryChunkEnumerator<T1, T2> QueryChunks<T1, T2>()
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
    {
        return new QueryChunkEnumerator<T1, T2>(Archetypes.AsSpan(), Schema.GetComponentId<T1>(), Schema.GetComponentId<T2>());
    }

    public QueryChunkEnumerator<T1, T2, T3> QueryChunks<T1, T2, T3>()
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
        where T3 : unmanaged, IComponent<T3>
    {
        return new QueryChunkEnumerator<T1, T2, T3>(
            Archetypes.AsSpan(),
            Schema.GetComponentId<T1>(),
            Schema.GetComponentId<T2>(),
            Schema.GetComponentId<T3>());
    }

    public readonly ref struct QueryChunk<T>
        where T : unmanaged, IComponent<T>
    {
        public readonly ReadOnlySpan<Entity> Entities;
        public readonly ReadOnlySpan<T> Component1;

        internal QueryChunk(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> component1)
        {
            Entities = entities;
            Component1 = component1;
        }
    }

    public readonly ref struct QueryChunk<T1, T2>
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
    {
        public readonly ReadOnlySpan<Entity> Entities;
        public readonly ReadOnlySpan<T1> Component1;
        public readonly ReadOnlySpan<T2> Component2;

        internal QueryChunk(ReadOnlySpan<Entity> entities, ReadOnlySpan<T1> component1, ReadOnlySpan<T2> component2)
        {
            Entities = entities;
            Component1 = component1;
            Component2 = component2;
        }
    }

    public readonly ref struct QueryChunk<T1, T2, T3>
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
        where T3 : unmanaged, IComponent<T3>
    {
        public readonly ReadOnlySpan<Entity> Entities;
        public readonly ReadOnlySpan<T1> Component1;
        public readonly ReadOnlySpan<T2> Component2;
        public readonly ReadOnlySpan<T3> Component3;

        internal QueryChunk(
            ReadOnlySpan<Entity> entities,
            ReadOnlySpan<T1> component1,
            ReadOnlySpan<T2> component2,
            ReadOnlySpan<T3> component3)
        {
            Entities = entities;
            Component1 = component1;
            Component2 = component2;
            Component3 = component3;
        }
    }

    public ref struct QueryChunkEnumerator<T>
        where T : unmanaged, IComponent<T>
    {
        private readonly ReadOnlySpan<Archetype> _archetypes;
        private readonly ComponentId _id;
        private int _index;
        private QueryChunk<T> _current;

        internal QueryChunkEnumerator(ReadOnlySpan<Archetype> archetypes, ComponentId id)
        {
            _archetypes = archetypes;
            _id = id;
            _index = -1;
            _current = default;
        }

        public QueryChunk<T> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public QueryChunkEnumerator<T> GetEnumerator() => this;

        public bool MoveNext()
        {
            while (++_index < _archetypes.Length)
            {
                Archetype archetype = _archetypes[_index];
                if (!archetype.HasComponent(_id) || archetype.Count == 0)
                    continue;

                _current = new QueryChunk<T>(archetype.GetEntitySpan(), archetype.GetComponentSpan<T>(_id));
                return true;
            }

            return false;
        }
    }

    public ref struct QueryChunkEnumerator<T1, T2>
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
    {
        private readonly ReadOnlySpan<Archetype> _archetypes;
        private readonly ComponentId _id1;
        private readonly ComponentId _id2;
        private int _index;
        private QueryChunk<T1, T2> _current;

        internal QueryChunkEnumerator(ReadOnlySpan<Archetype> archetypes, ComponentId id1, ComponentId id2)
        {
            _archetypes = archetypes;
            _id1 = id1;
            _id2 = id2;
            _index = -1;
            _current = default;
        }

        public QueryChunk<T1, T2> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public QueryChunkEnumerator<T1, T2> GetEnumerator() => this;

        public bool MoveNext()
        {
            while (++_index < _archetypes.Length)
            {
                Archetype archetype = _archetypes[_index];
                if (!archetype.HasComponent(_id1) || !archetype.HasComponent(_id2) || archetype.Count == 0)
                    continue;

                _current = new QueryChunk<T1, T2>(
                    archetype.GetEntitySpan(),
                    archetype.GetComponentSpan<T1>(_id1),
                    archetype.GetComponentSpan<T2>(_id2));
                return true;
            }

            return false;
        }
    }

    public ref struct QueryChunkEnumerator<T1, T2, T3>
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
        where T3 : unmanaged, IComponent<T3>
    {
        private readonly ReadOnlySpan<Archetype> _archetypes;
        private readonly ComponentId _id1;
        private readonly ComponentId _id2;
        private readonly ComponentId _id3;
        private int _index;
        private QueryChunk<T1, T2, T3> _current;

        internal QueryChunkEnumerator(ReadOnlySpan<Archetype> archetypes, ComponentId id1, ComponentId id2, ComponentId id3)
        {
            _archetypes = archetypes;
            _id1 = id1;
            _id2 = id2;
            _id3 = id3;
            _index = -1;
            _current = default;
        }

        public QueryChunk<T1, T2, T3> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public QueryChunkEnumerator<T1, T2, T3> GetEnumerator() => this;

        public bool MoveNext()
        {
            while (++_index < _archetypes.Length)
            {
                Archetype archetype = _archetypes[_index];
                if (!archetype.HasComponent(_id1) || !archetype.HasComponent(_id2) || !archetype.HasComponent(_id3) || archetype.Count == 0)
                    continue;

                _current = new QueryChunk<T1, T2, T3>(
                    archetype.GetEntitySpan(),
                    archetype.GetComponentSpan<T1>(_id1),
                    archetype.GetComponentSpan<T2>(_id2),
                    archetype.GetComponentSpan<T3>(_id3));
                return true;
            }

            return false;
        }
    }
}
