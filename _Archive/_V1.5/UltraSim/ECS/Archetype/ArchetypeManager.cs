using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS
{
    /// <summary>
    /// ECS internal archetype index/storage manager.
    /// Not intended as a direct mutation API for gameplay code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class ArchetypeManager
    {
        private const int StackAllocThreshold = 64;

        private readonly List<Archetype> _archetypes;
        private readonly Dictionary<ArchetypeSignature, Archetype> _bySignature;

        private Archetype? _emptyArchetype;

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _archetypes.Count;
        }

        public IReadOnlyList<Archetype> Archetypes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _archetypes;
        }

        internal ArchetypeManager()
        {
            _archetypes = new List<Archetype>(64);
            _bySignature = new Dictionary<ArchetypeSignature, Archetype>(64);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Archetype GetOrCreateEmpty()
        {
            if (_emptyArchetype is not null)
                return _emptyArchetype;

            return GetOrCreateEmptySlow();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Archetype GetOrCreateEmptySlow()
        {
            var emptySignature = new ArchetypeSignature(ReadOnlySpan<ComponentId>.Empty);
            if (_bySignature.TryGetValue(emptySignature, out var existing))
            {
                _emptyArchetype = existing;
                return existing;
            }

            var archetype = new Archetype(emptySignature, Array.Empty<ComponentId>());
            archetype.InitializeColumns(ComponentManager.CreateColumn);
            _bySignature.Add(emptySignature, archetype);
            _archetypes.Add(archetype);
            _emptyArchetype = archetype;
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

            var rented = ArrayPool<ComponentId>.Shared.Rent(componentIds.Length);
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
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));

            if (_bySignature.TryGetValue(signature, out var existing))
                return existing;

            if (signature.ComponentCount == 0)
                return GetOrCreateEmpty();

            var ids = new ComponentId[signature.ComponentCount];
            int write = 0;

            var bits = signature.BitsSpan;
            for (int word = 0; word < bits.Length; word++)
            {
                ulong value = bits[word];
                while (value != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(value);
                    int idValue = (word << 6) + bit;
                    ids[write++] = new ComponentId(idValue);
                    value &= value - 1;
                }
            }

            if (write != ids.Length)
                Array.Resize(ref ids, write);

            var archetype = new Archetype(signature, ids);
            archetype.InitializeColumns(ComponentManager.CreateColumn);
            _bySignature.Add(signature, archetype);
            _archetypes.Add(archetype);
            return archetype;
        }

        private Archetype GetOrCreateWithBuffer(ReadOnlySpan<ComponentId> componentIds, Span<ComponentId> workBuffer)
        {
            componentIds.CopyTo(workBuffer);
            int normalizedLen = NormalizeInPlace(workBuffer);
            var normalizedSpan = workBuffer.Slice(0, normalizedLen);

            var signature = CreateSignature(normalizedSpan);
            if (_bySignature.TryGetValue(signature, out var existing))
                return existing;

            var finalIds = normalizedSpan.ToArray();
            var archetype = new Archetype(signature, finalIds);
            archetype.InitializeColumns(ComponentManager.CreateColumn);
            _bySignature.Add(signature, archetype);
            _archetypes.Add(archetype);
            return archetype;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetBySignature(ArchetypeSignature signature, out Archetype archetype)
            => _bySignature.TryGetValue(signature, out archetype!);

        public int FindArchetypesWithAll(ReadOnlySpan<ComponentId> required, Span<Archetype> destination)
        {
            int written = 0;
            var archetypes = CollectionsMarshal.AsSpan(_archetypes);

            for (int i = 0; i < archetypes.Length && written < destination.Length; i++)
            {
                var arch = archetypes[i];
                if (arch.Signature.HasAll(required))
                    destination[written++] = arch;
            }

            return written;
        }

        public ArchetypeEnumerator EnumerateWith(ReadOnlySpan<ComponentId> required)
            => new ArchetypeEnumerator(this, required);

        public ref struct ArchetypeEnumerator
        {
            private readonly ReadOnlySpan<Archetype> _archetypes;
            private readonly ReadOnlySpan<ComponentId> _required;
            private int _index;
            private Archetype? _current;

            internal ArchetypeEnumerator(ArchetypeManager manager, ReadOnlySpan<ComponentId> required)
            {
                _archetypes = CollectionsMarshal.AsSpan(manager._archetypes);
                _required = required;
                _index = -1;
                _current = null;
            }

            public Archetype Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _current!;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (++_index < _archetypes.Length)
                {
                    var arch = _archetypes[_index];
                    if (arch.Signature.HasAll(_required))
                    {
                        _current = arch;
                        return true;
                    }
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ArchetypeEnumerator GetEnumerator() => this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NormalizeInPlace(Span<ComponentId> ids)
        {
            if (ids.Length <= 1)
                return ids.Length;

            SortComponentIds(ids);

            int write = 0;
            int prev = int.MinValue;

            for (int i = 0; i < ids.Length; i++)
            {
                int current = ids[i].Value;
                if (current < 0)
                    throw new InvalidOperationException("ComponentId.Invalid is not allowed.");

                if (current == prev)
                    continue;

                prev = current;
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
                    var key = ids[i];
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
            int maxId = normalized[normalized.Length - 1].Value;
            int arrayLen = (maxId >> 6) + 1;
            var bits = new ulong[arrayLen];

            for (int i = 0; i < normalized.Length; i++)
            {
                int id = normalized[i].Value;
                bits[id >> 6] |= 1UL << (id & 63);
            }

            int hashCode = ComputeHash(bits);
            return new ArchetypeSignature(bits, normalized.Length, hashCode);
        }

        private static int ComputeHash(ulong[] bits)
        {
            uint hash = 2166136261;
            for (int i = 0; i < bits.Length; i++)
            {
                ulong v = bits[i];
                hash = (hash ^ (uint)v) * 16777619;
                hash = (hash ^ (uint)(v >> 32)) * 16777619;
            }
            return (int)hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<Archetype> AsSpan() => CollectionsMarshal.AsSpan(_archetypes);
    }
}

