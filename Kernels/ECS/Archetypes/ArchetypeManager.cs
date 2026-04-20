using System.Buffers;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

public sealed class ArchetypeManager
{
    private const int StackAllocThreshold = 64;

    private readonly EcsSchema _schema;
    private readonly List<Archetype> _archetypes;
    private readonly Dictionary<ArchetypeSignature, Archetype> _bySignature;
    private Archetype? _emptyArchetype;
    private int _version;

    internal ArchetypeManager(EcsSchema schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _archetypes = new List<Archetype>(64);
        _bySignature = new Dictionary<ArchetypeSignature, Archetype>(64);
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _archetypes.Count;
    }

    public int Version
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _version;
    }

    public IReadOnlyList<Archetype> Archetypes => new ReadOnlyCollection<Archetype>(_archetypes);

    internal Archetype GetOrCreateEmpty()
    {
        if (_emptyArchetype is not null)
            return _emptyArchetype;

        return GetOrCreateEmptySlow();
    }

    private Archetype GetOrCreateEmptySlow()
    {
        var emptySignature = new ArchetypeSignature(ReadOnlySpan<ComponentId>.Empty);
        if (_bySignature.TryGetValue(emptySignature, out Archetype? existing))
        {
            _emptyArchetype = existing;
            return existing;
        }

        var archetype = new Archetype(emptySignature, Array.Empty<ComponentId>(), Array.Empty<IComponentColumn>());
        _bySignature.Add(emptySignature, archetype);
        _archetypes.Add(archetype);
        _emptyArchetype = archetype;
        _version++;
        return archetype;
    }

    internal Archetype GetOrCreate(ReadOnlySpan<ComponentId> componentIds)
    {
        if (componentIds.Length == 0)
            return GetOrCreateEmpty();

        if (componentIds.Length <= StackAllocThreshold)
        {
            Span<ComponentId> buffer = stackalloc ComponentId[componentIds.Length];
            return GetOrCreateWithBuffer(componentIds, buffer);
        }

        ComponentId[] rented = ArrayPool<ComponentId>.Shared.Rent(componentIds.Length);
        try
        {
            return GetOrCreateWithBuffer(componentIds, rented.AsSpan(0, componentIds.Length));
        }
        finally
        {
            ArrayPool<ComponentId>.Shared.Return(rented);
        }
    }

    internal Archetype GetOrCreate(ArchetypeSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        if (_bySignature.TryGetValue(signature, out Archetype? existing))
            return existing;

        if (signature.ComponentCount == 0)
            return GetOrCreateEmpty();

        var ids = new ComponentId[signature.ComponentCount];
        int write = 0;
        ReadOnlySpan<ulong> bits = signature.BitsSpan;

        for (int word = 0; word < bits.Length; word++)
        {
            ulong value = bits[word];
            while (value != 0)
            {
                int bit = BitOperations.TrailingZeroCount(value);
                ids[write++] = new ComponentId((word << 6) + bit);
                value &= value - 1;
            }
        }

        if (write != ids.Length)
            Array.Resize(ref ids, write);

        return CreateArchetype(signature, ids);
    }

    public bool TryGetBySignature(ArchetypeSignature signature, out Archetype archetype)
        => _bySignature.TryGetValue(signature, out archetype!);

    public ReadOnlySpan<Archetype> AsSpan() => CollectionsMarshal.AsSpan(_archetypes);

    public ArchetypeEnumerator EnumerateWith(ReadOnlySpan<ComponentId> required)
        => new(this, required);

    private Archetype GetOrCreateWithBuffer(ReadOnlySpan<ComponentId> componentIds, Span<ComponentId> workBuffer)
    {
        componentIds.CopyTo(workBuffer);
        int normalizedLength = NormalizeInPlace(workBuffer);
        ReadOnlySpan<ComponentId> normalized = workBuffer.Slice(0, normalizedLength);

        ArchetypeSignature signature = CreateSignature(normalized);
        if (_bySignature.TryGetValue(signature, out Archetype? existing))
            return existing;

        return CreateArchetype(signature, normalized.ToArray());
    }

    private Archetype CreateArchetype(ArchetypeSignature signature, ComponentId[] componentIds)
    {
        var columns = new IComponentColumn[componentIds.Length];
        for (int i = 0; i < componentIds.Length; i++)
            columns[i] = _schema.CreateColumn(componentIds[i], 0);

        var archetype = new Archetype(signature, componentIds, columns);
        _bySignature.Add(signature, archetype);
        _archetypes.Add(archetype);
        _version++;
        return archetype;
    }

    private static int NormalizeInPlace(Span<ComponentId> ids)
    {
        if (ids.Length <= 1)
            return ids.Length;

        SortComponentIds(ids);

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

    private static void SortComponentIds(Span<ComponentId> ids)
    {
        if (ids.Length <= 16)
        {
            for (int i = 1; i < ids.Length; i++)
            {
                ComponentId key = ids[i];
                int j = i - 1;
                while (j >= 0 && ids[j].Value > key.Value)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }

                ids[j + 1] = key;
            }
        }
        else
        {
            MemoryExtensions.Sort(ids, static (a, b) => a.Value.CompareTo(b.Value));
        }
    }

    private static ArchetypeSignature CreateSignature(ReadOnlySpan<ComponentId> normalized)
    {
        int maxId = normalized[^1].Value;
        int arrayLength = (maxId >> 6) + 1;
        var bits = new ulong[arrayLength];

        for (int i = 0; i < normalized.Length; i++)
        {
            int id = normalized[i].Value;
            bits[id >> 6] |= 1UL << (id & 63);
        }

        return new ArchetypeSignature(bits, normalized.Length, ComputeHash(bits));
    }

    private static int ComputeHash(ulong[] bits)
    {
        uint hash = 2166136261;
        for (int i = 0; i < bits.Length; i++)
        {
            ulong value = bits[i];
            hash = (hash ^ (uint)value) * 16777619;
            hash = (hash ^ (uint)(value >> 32)) * 16777619;
        }

        return (int)hash;
    }

    public ref struct ArchetypeEnumerator
    {
        private readonly ReadOnlySpan<Archetype> _archetypes;
        private int _index;
        private Archetype? _current;

        internal ArchetypeEnumerator(ArchetypeManager manager, ReadOnlySpan<ComponentId> required)
        {
            _archetypes = CollectionsMarshal.AsSpan(manager._archetypes);
            _index = -1;
            _current = null;
        }

        internal ArchetypeEnumerator(ReadOnlySpan<Archetype> archetypes)
        {
            _archetypes = archetypes;
            _index = -1;
            _current = null;
        }

        public Archetype Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArchetypeEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (++_index >= _archetypes.Length)
                return false;

            _current = _archetypes[_index];
            return true;
        }
    }
}
