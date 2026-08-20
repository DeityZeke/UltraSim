using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

public sealed partial class World
{
    // Query plans are intentionally preferred unconditionally in the current kernel. The older direct-scan
    // path remains as dormant scaffolding for future re-evaluation, so the threshold stays at zero by design.
    private const int DirectQueryArchetypeThreshold = 0;
    [ThreadStatic] private static int[]? s_threadDirtyIndexBuffer;
    [ThreadStatic] private static bool s_threadDirtyIndexBufferInUse;

    private static DirtyIndexBufferLease RentDirtyIndexBuffer(int entityCount)
    {
        int minimumLength = Math.Max(1, entityCount);
        if (!s_threadDirtyIndexBufferInUse)
        {
            int[]? buffer = s_threadDirtyIndexBuffer;
            if (buffer is null || buffer.Length < minimumLength)
            {
                buffer = new int[minimumLength];
                s_threadDirtyIndexBuffer = buffer;
            }

            s_threadDirtyIndexBufferInUse = true;
            return new DirtyIndexBufferLease(buffer, fromPool: false);
        }

        return new DirtyIndexBufferLease(ArrayPool<int>.Shared.Rent(minimumLength), fromPool: true);
    }

    private readonly ref struct DirtyIndexBufferLease
    {
        private readonly int[] _buffer;
        private readonly bool _fromPool;

        public DirtyIndexBufferLease(int[] buffer, bool fromPool)
        {
            _buffer = buffer;
            _fromPool = fromPool;
        }

        public Span<int> AsSpan(int length) => _buffer.AsSpan(0, length);

        public void Dispose()
        {
            if (_fromPool)
            {
                ArrayPool<int>.Shared.Return(_buffer, clearArray: false);
                return;
            }

            s_threadDirtyIndexBufferInUse = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArchetypeManager.ArchetypeEnumerator QueryArchetypes(ReadOnlySpan<ComponentId> required)
    {
        return new ArchetypeManager.ArchetypeEnumerator(GetCachedQueryArchetypes(required));
    }

    public int Query<T>(ReadOnlyQueryAction<T> action)
        where T : unmanaged, IComponent<T>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId id = Schema.GetComponentId<T>();
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        if (archetypes.Length <= DirectQueryArchetypeThreshold)
        {
            int directMatched = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypes.Length; archetypeIndex++)
            {
                Archetype archetype = archetypes[archetypeIndex];
                int column = archetype.GetColumnIndex(id);
                if (column < 0)
                    continue;

                ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
                ReadOnlySpan<T> values = archetype.GetComponentSpanAt<T>(column);
                int count = entities.Length;
                for (int i = 0; i < count; i++)
                    action(entities[i], in values[i]);

                directMatched += count;
            }

            return directMatched;
        }

        ReadOnlySpan<QueryPlan1> plans = GetCachedQueryPlan(id);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan1 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            ReadOnlySpan<T> values = archetype.GetComponentSpanAt<T>(plan.Column1);
            int count = entities.Length;
            for (int i = 0; i < count; i++)
                action(entities[i], in values[i]);

            matched += count;
        }

        return matched;
    }

    public int Query<T, TVisitor>(ref TVisitor visitor)
        where T : unmanaged, IComponent<T>
        where TVisitor : struct, IReadOnlyEntityVisitor<T>
    {
        ReadOnlySpan<QueryPlan1> plans = GetCachedQueryPlan(Schema.GetComponentId<T>());
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan1 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            ReadOnlySpan<T> values = archetype.GetComponentSpanAt<T>(plan.Column1);
            int count = entities.Length;
            for (int i = 0; i < count; i++)
                visitor.Visit(entities[i], in values[i]);

            matched += count;
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
        if (archetypes.Length <= DirectQueryArchetypeThreshold)
        {
            int directMatched = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypes.Length; archetypeIndex++)
            {
                Archetype archetype = archetypes[archetypeIndex];
                int column1 = archetype.GetColumnIndex(id1);
                if (column1 < 0)
                    continue;

                int column2 = archetype.GetColumnIndex(id2);
                if (column2 < 0)
                    continue;

                ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
                ReadOnlySpan<T1> values1 = archetype.GetComponentSpanAt<T1>(column1);
                ReadOnlySpan<T2> values2 = archetype.GetComponentSpanAt<T2>(column2);
                int count = entities.Length;
                for (int i = 0; i < count; i++)
                    action(entities[i], in values1[i], in values2[i]);

                directMatched += count;
            }

            return directMatched;
        }

        ReadOnlySpan<QueryPlan2> plans = GetCachedQueryPlan(id1, id2);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan2 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            ReadOnlySpan<T1> values1 = archetype.GetComponentSpanAt<T1>(plan.Column1);
            ReadOnlySpan<T2> values2 = archetype.GetComponentSpanAt<T2>(plan.Column2);
            int count = entities.Length;
            for (int i = 0; i < count; i++)
                action(entities[i], in values1[i], in values2[i]);

            matched += count;
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
        if (archetypes.Length <= DirectQueryArchetypeThreshold)
        {
            int directMatched = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypes.Length; archetypeIndex++)
            {
                Archetype archetype = archetypes[archetypeIndex];
                int column1 = archetype.GetColumnIndex(id1);
                if (column1 < 0)
                    continue;

                int column2 = archetype.GetColumnIndex(id2);
                if (column2 < 0)
                    continue;

                int column3 = archetype.GetColumnIndex(id3);
                if (column3 < 0)
                    continue;

                ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
                ReadOnlySpan<T1> values1 = archetype.GetComponentSpanAt<T1>(column1);
                ReadOnlySpan<T2> values2 = archetype.GetComponentSpanAt<T2>(column2);
                ReadOnlySpan<T3> values3 = archetype.GetComponentSpanAt<T3>(column3);
                int count = entities.Length;
                for (int i = 0; i < count; i++)
                    action(entities[i], in values1[i], in values2[i], in values3[i]);

                directMatched += count;
            }

            return directMatched;
        }

        ReadOnlySpan<QueryPlan3> plans = GetCachedQueryPlan(id1, id2, id3);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan3 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            ReadOnlySpan<T1> values1 = archetype.GetComponentSpanAt<T1>(plan.Column1);
            ReadOnlySpan<T2> values2 = archetype.GetComponentSpanAt<T2>(plan.Column2);
            ReadOnlySpan<T3> values3 = archetype.GetComponentSpanAt<T3>(plan.Column3);
            int count = entities.Length;
            for (int i = 0; i < count; i++)
                action(entities[i], in values1[i], in values2[i], in values3[i]);

            matched += count;
        }

        return matched;
    }

    public int ReadChunks<T>(ReadOnlyChunkAction<T> action)
        where T : unmanaged, IComponent<T>
    {
        ArgumentNullException.ThrowIfNull(action);

        ReadOnlySpan<QueryPlan1> plans = GetCachedQueryPlan(Schema.GetComponentId<T>());
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan1 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            if (entities.Length == 0)
                continue;

            action(entities, archetype.GetComponentSpanAt<T>(plan.Column1));
            matched += entities.Length;
        }

        return matched;
    }

    public int ReadChunks<T1, T2>(ReadOnlyChunkAction<T1, T2> action)
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
    {
        ArgumentNullException.ThrowIfNull(action);

        ReadOnlySpan<QueryPlan2> plans = GetCachedQueryPlan(Schema.GetComponentId<T1>(), Schema.GetComponentId<T2>());
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan2 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            if (entities.Length == 0)
                continue;

            action(entities, archetype.GetComponentSpanAt<T1>(plan.Column1), archetype.GetComponentSpanAt<T2>(plan.Column2));
            matched += entities.Length;
        }

        return matched;
    }

    public int ReadChunks<T1, T2, T3>(ReadOnlyChunkAction<T1, T2, T3> action)
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
        where T3 : unmanaged, IComponent<T3>
    {
        ArgumentNullException.ThrowIfNull(action);

        ReadOnlySpan<QueryPlan3> plans = GetCachedQueryPlan(Schema.GetComponentId<T1>(), Schema.GetComponentId<T2>(), Schema.GetComponentId<T3>());
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan3 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            if (entities.Length == 0)
                continue;

            action(
                entities,
                archetype.GetComponentSpanAt<T1>(plan.Column1),
                archetype.GetComponentSpanAt<T2>(plan.Column2),
                archetype.GetComponentSpanAt<T3>(plan.Column3));
            matched += entities.Length;
        }

        return matched;
    }

    public int PatchQuery<TRead, TWrite>(PatchQueryAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId = Schema.GetComponentId<TRead>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        if (archetypes.Length <= DirectQueryArchetypeThreshold)
        {
            int directMatched = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypes.Length; archetypeIndex++)
            {
                Archetype archetype = archetypes[archetypeIndex];
                int readColumn = archetype.GetColumnIndex(readId);
                if (readColumn < 0)
                    continue;

                int writeColumn = archetype.GetColumnIndex(writeId);
                if (writeColumn < 0)
                    continue;

                ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
                ReadOnlySpan<TRead> reads = archetype.GetComponentSpanAt<TRead>(readColumn);
                Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(writeColumn);
                for (int i = 0; i < entities.Length; i++)
                {
                    TRead read = reads[i];
                    action(entities[i], in read, ref writes[i]);
                }

                _dirty.MarkUnique(writeId, entities);
                directMatched += entities.Length;
            }

            return directMatched;
        }

        ReadOnlySpan<QueryPlan2> plans = GetCachedQueryPlan(readId, writeId);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan2 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            ReadOnlySpan<TRead> reads = archetype.GetComponentSpanAt<TRead>(plan.Column1);
            Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(plan.Column2);
            for (int i = 0; i < entities.Length; i++)
            {
                TRead read = reads[i];
                action(entities[i], in read, ref writes[i]);
            }

            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    public int PatchQuery<TRead1, TRead2, TWrite>(PatchQueryAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId1 = Schema.GetComponentId<TRead1>();
        ComponentId readId2 = Schema.GetComponentId<TRead2>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<Archetype> archetypes = Archetypes.AsSpan();
        if (archetypes.Length <= DirectQueryArchetypeThreshold)
        {
            int directMatched = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypes.Length; archetypeIndex++)
            {
                Archetype archetype = archetypes[archetypeIndex];
                int readColumn1 = archetype.GetColumnIndex(readId1);
                if (readColumn1 < 0)
                    continue;

                int readColumn2 = archetype.GetColumnIndex(readId2);
                if (readColumn2 < 0)
                    continue;

                int writeColumn = archetype.GetColumnIndex(writeId);
                if (writeColumn < 0)
                    continue;

                ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
                ReadOnlySpan<TRead1> reads1 = archetype.GetComponentSpanAt<TRead1>(readColumn1);
                ReadOnlySpan<TRead2> reads2 = archetype.GetComponentSpanAt<TRead2>(readColumn2);
                Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(writeColumn);
                for (int i = 0; i < entities.Length; i++)
                {
                    TRead1 read1 = reads1[i];
                    TRead2 read2 = reads2[i];
                    action(entities[i], in read1, in read2, ref writes[i]);
                }

                _dirty.MarkUnique(writeId, entities);
                directMatched += entities.Length;
            }

            return directMatched;
        }

        ReadOnlySpan<QueryPlan3> plans = GetCachedQueryPlan(readId1, readId2, writeId);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan3 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            ReadOnlySpan<TRead1> reads1 = archetype.GetComponentSpanAt<TRead1>(plan.Column1);
            ReadOnlySpan<TRead2> reads2 = archetype.GetComponentSpanAt<TRead2>(plan.Column2);
            Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(plan.Column3);
            for (int i = 0; i < entities.Length; i++)
            {
                TRead1 read1 = reads1[i];
                TRead2 read2 = reads2[i];
                action(entities[i], in read1, in read2, ref writes[i]);
            }

            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    public int PatchChunks<TWrite>(PatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan1> plans = GetCachedQueryPlan(writeId);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan1 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            if (entities.Length == 0)
                continue;

            Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(plan.Column1);
            action(entities, writes);
            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    public int PatchChunksSparse<TWrite>(SparsePatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan1> plans = GetCachedQueryPlan(writeId);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan1 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            using DirtyIndexBufferLease dirtyBuffer = RentDirtyIndexBuffer(entityCount);
            {
                Span<int> dirtyIndices = dirtyBuffer.AsSpan(entityCount);
                int dirtyCount = action(entities, archetype.GetComponentSpanAt<TWrite>(plan.Column1), dirtyIndices);
                if (dirtyCount > 0)
                {
                    _dirty.Mark(writeId, entities, dirtyIndices[..dirtyCount]);
                    matched += dirtyCount;
                }
            }
        }

        return matched;
    }

    public int PatchChunks<TWrite>(PatchChunkPlanCache<TWrite> cache, PatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan1> plans = GetCachedQueryPlan(cache);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan1 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            if (entities.Length == 0)
                continue;

            Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(plan.Column1);
            action(entities, writes);
            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    public int PatchChunks<TRead, TWrite>(PatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId = Schema.GetComponentId<TRead>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan2> plans = GetCachedQueryPlan(readId, writeId);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan2 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            if (entities.Length == 0)
                continue;

            ReadOnlySpan<TRead> reads = archetype.GetComponentSpanAt<TRead>(plan.Column1);
            Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(plan.Column2);
            action(entities, reads, writes);
            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    public int PatchChunksSparse<TRead, TWrite>(SparsePatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId = Schema.GetComponentId<TRead>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan2> plans = GetCachedQueryPlan(readId, writeId);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan2 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            using DirtyIndexBufferLease dirtyBuffer = RentDirtyIndexBuffer(entityCount);
            {
                Span<int> dirtyIndices = dirtyBuffer.AsSpan(entityCount);
                int dirtyCount = action(
                    entities,
                    archetype.GetComponentSpanAt<TRead>(plan.Column1),
                    archetype.GetComponentSpanAt<TWrite>(plan.Column2),
                    dirtyIndices);
                if (dirtyCount > 0)
                {
                    _dirty.Mark(writeId, entities, dirtyIndices[..dirtyCount]);
                    matched += dirtyCount;
                }
            }
        }

        return matched;
    }

    public int PatchChunks<TRead, TWrite>(PatchChunkPlanCache<TRead, TWrite> cache, PatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan2> plans = GetCachedQueryPlan(cache);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan2 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            if (entities.Length == 0)
                continue;

            ReadOnlySpan<TRead> reads = archetype.GetComponentSpanAt<TRead>(plan.Column1);
            Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(plan.Column2);
            action(entities, reads, writes);
            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    public int PatchChunks<TRead1, TRead2, TWrite>(PatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId1 = Schema.GetComponentId<TRead1>();
        ComponentId readId2 = Schema.GetComponentId<TRead2>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan3> plans = GetCachedQueryPlan(readId1, readId2, writeId);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan3 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            if (entities.Length == 0)
                continue;

            ReadOnlySpan<TRead1> reads1 = archetype.GetComponentSpanAt<TRead1>(plan.Column1);
            ReadOnlySpan<TRead2> reads2 = archetype.GetComponentSpanAt<TRead2>(plan.Column2);
            Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(plan.Column3);
            action(entities, reads1, reads2, writes);
            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    public int PatchChunksSparse<TRead1, TRead2, TWrite>(SparsePatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId1 = Schema.GetComponentId<TRead1>();
        ComponentId readId2 = Schema.GetComponentId<TRead2>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan3> plans = GetCachedQueryPlan(readId1, readId2, writeId);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan3 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            using DirtyIndexBufferLease dirtyBuffer = RentDirtyIndexBuffer(entityCount);
            {
                Span<int> dirtyIndices = dirtyBuffer.AsSpan(entityCount);
                int dirtyCount = action(
                    entities,
                    archetype.GetComponentSpanAt<TRead1>(plan.Column1),
                    archetype.GetComponentSpanAt<TRead2>(plan.Column2),
                    archetype.GetComponentSpanAt<TWrite>(plan.Column3),
                    dirtyIndices);
                if (dirtyCount > 0)
                {
                    _dirty.Mark(writeId, entities, dirtyIndices[..dirtyCount]);
                    matched += dirtyCount;
                }
            }
        }

        return matched;
    }

    public int PatchChunks<TRead1, TRead2, TWrite>(PatchChunkPlanCache<TRead1, TRead2, TWrite> cache, PatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan3> plans = GetCachedQueryPlan(cache);
        int matched = 0;

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan3 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            if (entities.Length == 0)
                continue;

            ReadOnlySpan<TRead1> reads1 = archetype.GetComponentSpanAt<TRead1>(plan.Column1);
            ReadOnlySpan<TRead2> reads2 = archetype.GetComponentSpanAt<TRead2>(plan.Column2);
            Span<TWrite> writes = archetype.GetComponentSpanAt<TWrite>(plan.Column3);
            action(entities, reads1, reads2, writes);
            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    internal int PatchChunksParallel<TWrite>(Action<int, Action<int>>? parallelFor, int blockSize, PatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan1> plans = GetCachedQueryPlan(writeId);
        int matched = 0;
        int resolvedBlockSize = Math.Max(1, blockSize);

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan1 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            if (parallelFor is null || entityCount <= resolvedBlockSize)
            {
                action(entities, archetype.GetComponentSpanAt<TWrite>(plan.Column1));
            }
            else
            {
                int blockCount = (entityCount + resolvedBlockSize - 1) / resolvedBlockSize;
                parallelFor(blockCount, index =>
                {
                    int start = index * resolvedBlockSize;
                    int length = Math.Min(resolvedBlockSize, entityCount - start);
                    action(
                        archetype.GetEntitySpan().Slice(start, length),
                        archetype.GetComponentSpanAt<TWrite>(plan.Column1).Slice(start, length));
                });
            }

            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    internal int PatchChunksParallelSparse<TWrite>(Action<int, Action<int>>? parallelFor, int blockSize, SparsePatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan1> plans = GetCachedQueryPlan(writeId);
        int matched = 0;
        int resolvedBlockSize = Math.Max(1, blockSize);

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan1 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            if (parallelFor is null || entityCount <= resolvedBlockSize)
            {
                using DirtyIndexBufferLease dirtyBuffer = RentDirtyIndexBuffer(entityCount);
                {
                    Span<int> dirtyIndices = dirtyBuffer.AsSpan(entityCount);
                    int dirtyCount = action(entities, archetype.GetComponentSpanAt<TWrite>(plan.Column1), dirtyIndices);
                    if (dirtyCount > 0)
                    {
                        _dirty.Mark(writeId, entities, dirtyIndices[..dirtyCount]);
                        matched += dirtyCount;
                    }
                }
            }
            else
            {
                parallelFor((entityCount + resolvedBlockSize - 1) / resolvedBlockSize, index =>
                {
                    int start = index * resolvedBlockSize;
                    int length = Math.Min(resolvedBlockSize, entityCount - start);
                    using DirtyIndexBufferLease dirtyBuffer = RentDirtyIndexBuffer(length);
                    {
                        Span<int> dirtyIndices = dirtyBuffer.AsSpan(length);
                        int dirtyCount = action(
                            archetype.GetEntitySpan().Slice(start, length),
                            archetype.GetComponentSpanAt<TWrite>(plan.Column1).Slice(start, length),
                            dirtyIndices);
                        if (dirtyCount > 0)
                        {
                            _dirty.Mark(writeId, archetype.GetEntitySpan().Slice(start, length), dirtyIndices[..dirtyCount]);
                            Interlocked.Add(ref matched, dirtyCount);
                        }
                    }
                });
            }
        }

        return matched;
    }

    internal int PatchChunksParallel<TWrite>(PatchChunkPlanCache<TWrite> cache, Action<int, Action<int>>? parallelFor, int blockSize, PatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan1> plans = GetCachedQueryPlan(cache);
        int matched = 0;
        int resolvedBlockSize = Math.Max(1, blockSize);

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan1 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            if (parallelFor is null || entityCount <= resolvedBlockSize)
            {
                action(entities, archetype.GetComponentSpanAt<TWrite>(plan.Column1));
            }
            else
            {
                parallelFor((entityCount + resolvedBlockSize - 1) / resolvedBlockSize, index =>
                {
                    int start = index * resolvedBlockSize;
                    int length = Math.Min(resolvedBlockSize, entityCount - start);
                    action(
                        archetype.GetEntitySpan().Slice(start, length),
                        archetype.GetComponentSpanAt<TWrite>(plan.Column1).Slice(start, length));
                });
            }

            _dirty.MarkUnique(writeId, entities);
            matched += entityCount;
        }

        return matched;
    }

    internal int PatchChunksParallel<TRead, TWrite>(Action<int, Action<int>>? parallelFor, int blockSize, PatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId = Schema.GetComponentId<TRead>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan2> plans = GetCachedQueryPlan(readId, writeId);
        int matched = 0;
        int resolvedBlockSize = Math.Max(1, blockSize);

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan2 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            if (parallelFor is null || entityCount <= resolvedBlockSize)
            {
                action(
                    entities,
                    archetype.GetComponentSpanAt<TRead>(plan.Column1),
                    archetype.GetComponentSpanAt<TWrite>(plan.Column2));
            }
            else
            {
                int blockCount = (entityCount + resolvedBlockSize - 1) / resolvedBlockSize;
                parallelFor(blockCount, index =>
                {
                    int start = index * resolvedBlockSize;
                    int length = Math.Min(resolvedBlockSize, entityCount - start);
                    action(
                        archetype.GetEntitySpan().Slice(start, length),
                        archetype.GetComponentSpanAt<TRead>(plan.Column1).Slice(start, length),
                        archetype.GetComponentSpanAt<TWrite>(plan.Column2).Slice(start, length));
                });
            }

            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    internal int PatchChunksParallelSparse<TRead, TWrite>(Action<int, Action<int>>? parallelFor, int blockSize, SparsePatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId = Schema.GetComponentId<TRead>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan2> plans = GetCachedQueryPlan(readId, writeId);
        int matched = 0;
        int resolvedBlockSize = Math.Max(1, blockSize);

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan2 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            if (parallelFor is null || entityCount <= resolvedBlockSize)
            {
                using DirtyIndexBufferLease dirtyBuffer = RentDirtyIndexBuffer(entityCount);
                {
                    Span<int> dirtyIndices = dirtyBuffer.AsSpan(entityCount);
                    int dirtyCount = action(
                        entities,
                        archetype.GetComponentSpanAt<TRead>(plan.Column1),
                        archetype.GetComponentSpanAt<TWrite>(plan.Column2),
                        dirtyIndices);
                    if (dirtyCount > 0)
                    {
                        _dirty.Mark(writeId, entities, dirtyIndices[..dirtyCount]);
                        matched += dirtyCount;
                    }
                }
            }
            else
            {
                parallelFor((entityCount + resolvedBlockSize - 1) / resolvedBlockSize, index =>
                {
                    int start = index * resolvedBlockSize;
                    int length = Math.Min(resolvedBlockSize, entityCount - start);
                    using DirtyIndexBufferLease dirtyBuffer = RentDirtyIndexBuffer(length);
                    {
                        Span<int> dirtyIndices = dirtyBuffer.AsSpan(length);
                        int dirtyCount = action(
                            archetype.GetEntitySpan().Slice(start, length),
                            archetype.GetComponentSpanAt<TRead>(plan.Column1).Slice(start, length),
                            archetype.GetComponentSpanAt<TWrite>(plan.Column2).Slice(start, length),
                            dirtyIndices);
                        if (dirtyCount > 0)
                        {
                            _dirty.Mark(writeId, archetype.GetEntitySpan().Slice(start, length), dirtyIndices[..dirtyCount]);
                            Interlocked.Add(ref matched, dirtyCount);
                        }
                    }
                });
            }
        }

        return matched;
    }

    internal int PatchChunksParallel<TRead, TWrite>(PatchChunkPlanCache<TRead, TWrite> cache, Action<int, Action<int>>? parallelFor, int blockSize, PatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan2> plans = GetCachedQueryPlan(cache);
        int matched = 0;
        int resolvedBlockSize = Math.Max(1, blockSize);

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan2 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            if (parallelFor is null || entityCount <= resolvedBlockSize)
            {
                action(
                    entities,
                    archetype.GetComponentSpanAt<TRead>(plan.Column1),
                    archetype.GetComponentSpanAt<TWrite>(plan.Column2));
            }
            else
            {
                parallelFor((entityCount + resolvedBlockSize - 1) / resolvedBlockSize, index =>
                {
                    int start = index * resolvedBlockSize;
                    int length = Math.Min(resolvedBlockSize, entityCount - start);
                    action(
                        archetype.GetEntitySpan().Slice(start, length),
                        archetype.GetComponentSpanAt<TRead>(plan.Column1).Slice(start, length),
                        archetype.GetComponentSpanAt<TWrite>(plan.Column2).Slice(start, length));
                });
            }

            _dirty.MarkUnique(writeId, entities);
            matched += entityCount;
        }

        return matched;
    }

    internal int PatchChunksParallel<TRead1, TRead2, TWrite>(Action<int, Action<int>>? parallelFor, int blockSize, PatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId1 = Schema.GetComponentId<TRead1>();
        ComponentId readId2 = Schema.GetComponentId<TRead2>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan3> plans = GetCachedQueryPlan(readId1, readId2, writeId);
        int matched = 0;
        int resolvedBlockSize = Math.Max(1, blockSize);

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan3 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            if (parallelFor is null || entityCount <= resolvedBlockSize)
            {
                action(
                    entities,
                    archetype.GetComponentSpanAt<TRead1>(plan.Column1),
                    archetype.GetComponentSpanAt<TRead2>(plan.Column2),
                    archetype.GetComponentSpanAt<TWrite>(plan.Column3));
            }
            else
            {
                int blockCount = (entityCount + resolvedBlockSize - 1) / resolvedBlockSize;
                parallelFor(blockCount, index =>
                {
                    int start = index * resolvedBlockSize;
                    int length = Math.Min(resolvedBlockSize, entityCount - start);
                    action(
                        archetype.GetEntitySpan().Slice(start, length),
                        archetype.GetComponentSpanAt<TRead1>(plan.Column1).Slice(start, length),
                        archetype.GetComponentSpanAt<TRead2>(plan.Column2).Slice(start, length),
                        archetype.GetComponentSpanAt<TWrite>(plan.Column3).Slice(start, length));
                });
            }

            _dirty.MarkUnique(writeId, entities);
            matched += entities.Length;
        }

        return matched;
    }

    internal int PatchChunksParallelSparse<TRead1, TRead2, TWrite>(Action<int, Action<int>>? parallelFor, int blockSize, SparsePatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId readId1 = Schema.GetComponentId<TRead1>();
        ComponentId readId2 = Schema.GetComponentId<TRead2>();
        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan3> plans = GetCachedQueryPlan(readId1, readId2, writeId);
        int matched = 0;
        int resolvedBlockSize = Math.Max(1, blockSize);

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan3 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            if (parallelFor is null || entityCount <= resolvedBlockSize)
            {
                using DirtyIndexBufferLease dirtyBuffer = RentDirtyIndexBuffer(entityCount);
                {
                    Span<int> dirtyIndices = dirtyBuffer.AsSpan(entityCount);
                    int dirtyCount = action(
                        entities,
                        archetype.GetComponentSpanAt<TRead1>(plan.Column1),
                        archetype.GetComponentSpanAt<TRead2>(plan.Column2),
                        archetype.GetComponentSpanAt<TWrite>(plan.Column3),
                        dirtyIndices);
                    if (dirtyCount > 0)
                    {
                        _dirty.Mark(writeId, entities, dirtyIndices[..dirtyCount]);
                        matched += dirtyCount;
                    }
                }
            }
            else
            {
                parallelFor((entityCount + resolvedBlockSize - 1) / resolvedBlockSize, index =>
                {
                    int start = index * resolvedBlockSize;
                    int length = Math.Min(resolvedBlockSize, entityCount - start);
                    using DirtyIndexBufferLease dirtyBuffer = RentDirtyIndexBuffer(length);
                    {
                        Span<int> dirtyIndices = dirtyBuffer.AsSpan(length);
                        int dirtyCount = action(
                            archetype.GetEntitySpan().Slice(start, length),
                            archetype.GetComponentSpanAt<TRead1>(plan.Column1).Slice(start, length),
                            archetype.GetComponentSpanAt<TRead2>(plan.Column2).Slice(start, length),
                            archetype.GetComponentSpanAt<TWrite>(plan.Column3).Slice(start, length),
                            dirtyIndices);
                        if (dirtyCount > 0)
                        {
                            _dirty.Mark(writeId, archetype.GetEntitySpan().Slice(start, length), dirtyIndices[..dirtyCount]);
                            Interlocked.Add(ref matched, dirtyCount);
                        }
                    }
                });
            }
        }

        return matched;
    }

    internal int PatchChunksParallel<TRead1, TRead2, TWrite>(PatchChunkPlanCache<TRead1, TRead2, TWrite> cache, Action<int, Action<int>>? parallelFor, int blockSize, PatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(action);

        ComponentId writeId = Schema.GetComponentId<TWrite>();
        ReadOnlySpan<QueryPlan3> plans = GetCachedQueryPlan(cache);
        int matched = 0;
        int resolvedBlockSize = Math.Max(1, blockSize);

        for (int archetypeIndex = 0; archetypeIndex < plans.Length; archetypeIndex++)
        {
            QueryPlan3 plan = plans[archetypeIndex];
            Archetype archetype = plan.Archetype;
            ReadOnlySpan<Entity> entities = archetype.GetEntitySpan();
            int entityCount = entities.Length;
            if (entityCount == 0)
                continue;

            if (parallelFor is null || entityCount <= resolvedBlockSize)
            {
                action(
                    entities,
                    archetype.GetComponentSpanAt<TRead1>(plan.Column1),
                    archetype.GetComponentSpanAt<TRead2>(plan.Column2),
                    archetype.GetComponentSpanAt<TWrite>(plan.Column3));
            }
            else
            {
                parallelFor((entityCount + resolvedBlockSize - 1) / resolvedBlockSize, index =>
                {
                    int start = index * resolvedBlockSize;
                    int length = Math.Min(resolvedBlockSize, entityCount - start);
                    action(
                        archetype.GetEntitySpan().Slice(start, length),
                        archetype.GetComponentSpanAt<TRead1>(plan.Column1).Slice(start, length),
                        archetype.GetComponentSpanAt<TRead2>(plan.Column2).Slice(start, length),
                        archetype.GetComponentSpanAt<TWrite>(plan.Column3).Slice(start, length));
                });
            }

            _dirty.MarkUnique(writeId, entities);
            matched += entityCount;
        }

        return matched;
    }

    public QueryChunkEnumerator<T> QueryChunks<T>()
        where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        return new QueryChunkEnumerator<T>(GetCachedQueryPlan(id));
    }

    public QueryChunkEnumerator<T1, T2> QueryChunks<T1, T2>()
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
    {
        ComponentId id1 = Schema.GetComponentId<T1>();
        ComponentId id2 = Schema.GetComponentId<T2>();
        return new QueryChunkEnumerator<T1, T2>(GetCachedQueryPlan(id1, id2));
    }

    public QueryChunkEnumerator<T1, T2, T3> QueryChunks<T1, T2, T3>()
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
        where T3 : unmanaged, IComponent<T3>
    {
        ComponentId id1 = Schema.GetComponentId<T1>();
        ComponentId id2 = Schema.GetComponentId<T2>();
        ComponentId id3 = Schema.GetComponentId<T3>();
        return new QueryChunkEnumerator<T1, T2, T3>(GetCachedQueryPlan(id1, id2, id3));
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
        private readonly ReadOnlySpan<QueryPlan1> _plans;
        private int _index;
        private QueryChunk<T> _current;

        internal QueryChunkEnumerator(ReadOnlySpan<QueryPlan1> plans)
        {
            _plans = plans;
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
            while (++_index < _plans.Length)
            {
                QueryPlan1 plan = _plans[_index];
                Archetype archetype = plan.Archetype;
                if (archetype.Count == 0)
                    continue;

                _current = new QueryChunk<T>(archetype.GetEntitySpan(), archetype.GetComponentSpanAt<T>(plan.Column1));
                return true;
            }

            return false;
        }
    }

    public ref struct QueryChunkEnumerator<T1, T2>
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
    {
        private readonly ReadOnlySpan<QueryPlan2> _plans;
        private int _index;
        private QueryChunk<T1, T2> _current;

        internal QueryChunkEnumerator(ReadOnlySpan<QueryPlan2> plans)
        {
            _plans = plans;
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
            while (++_index < _plans.Length)
            {
                QueryPlan2 plan = _plans[_index];
                Archetype archetype = plan.Archetype;
                if (archetype.Count == 0)
                    continue;

                _current = new QueryChunk<T1, T2>(
                    archetype.GetEntitySpan(),
                    archetype.GetComponentSpanAt<T1>(plan.Column1),
                    archetype.GetComponentSpanAt<T2>(plan.Column2));
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
        private readonly ReadOnlySpan<QueryPlan3> _plans;
        private int _index;
        private QueryChunk<T1, T2, T3> _current;

        internal QueryChunkEnumerator(ReadOnlySpan<QueryPlan3> plans)
        {
            _plans = plans;
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
            while (++_index < _plans.Length)
            {
                QueryPlan3 plan = _plans[_index];
                Archetype archetype = plan.Archetype;
                if (archetype.Count == 0)
                    continue;

                _current = new QueryChunk<T1, T2, T3>(
                    archetype.GetEntitySpan(),
                    archetype.GetComponentSpanAt<T1>(plan.Column1),
                    archetype.GetComponentSpanAt<T2>(plan.Column2),
                    archetype.GetComponentSpanAt<T3>(plan.Column3));
                return true;
            }

            return false;
        }
    }
}
