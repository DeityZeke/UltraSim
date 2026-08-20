using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS
{
    public sealed class ArchetypeSignature : IEquatable<ArchetypeSignature>
    {
        private readonly ulong[] _bits;
        private readonly int _hashCode;
        private readonly int _componentCount;

        public int ComponentCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _componentCount;
        }

        public ArchetypeSignature(ReadOnlySpan<ComponentId> componentIds)
        {
            _componentCount = componentIds.Length;

            if (componentIds.Length == 0)
            {
                _bits = Array.Empty<ulong>();
                _hashCode = 0;
                return;
            }

            int maxId = 0;
            for (int i = 0; i < componentIds.Length; i++)
            {
                int id = componentIds[i].Value;
                if (id > maxId) maxId = id;
            }

            int arrayLen = (maxId >> 6) + 1;
            _bits = new ulong[arrayLen];

            for (int i = 0; i < componentIds.Length; i++)
            {
                int id = componentIds[i].Value;
                _bits[id >> 6] |= 1UL << (id & 63);
            }

            _hashCode = ComputeHashInline();
        }

        // Internal constructor for pre-computed bits (zero-alloc archetype creation)
        internal ArchetypeSignature(ulong[] bits, int componentCount, int hashCode)
        {
            _bits = bits;
            _componentCount = componentCount;
            _hashCode = hashCode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has(ComponentId componentId)
        {
            int id = componentId.Value;
            int idx = id >> 6;
            return (uint)idx < (uint)_bits.Length && (_bits[idx] & (1UL << (id & 63))) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAll(ReadOnlySpan<ComponentId> componentIds)
        {
            for (int i = 0; i < componentIds.Length; i++)
            {
                if (!Has(componentIds[i])) return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAny(ReadOnlySpan<ComponentId> componentIds)
        {
            for (int i = 0; i < componentIds.Length; i++)
            {
                if (Has(componentIds[i])) return true;
            }
            return false;
        }

        public bool IsSupersetOf(ArchetypeSignature other)
        {
            if (other._bits.Length > _bits.Length) return false;

            for (int i = 0; i < other._bits.Length; i++)
            {
                if ((other._bits[i] & ~_bits[i]) != 0) return false;
            }
            return true;
        }

        public ReadOnlySpan<ulong> BitsSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _bits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ArchetypeSignature? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (_hashCode != other._hashCode) return false;
            if (_bits.Length != other._bits.Length) return false;

            ref ulong a = ref MemoryMarshal.GetArrayDataReference(_bits);
            ref ulong b = ref MemoryMarshal.GetArrayDataReference(other._bits);

            for (int i = 0; i < _bits.Length; i++)
            {
                if (Unsafe.Add(ref a, i) != Unsafe.Add(ref b, i)) return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is ArchetypeSignature other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => _hashCode;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ComputeHashInline()
        {
            // FNV-1a style hash, faster than HashCode for small arrays
            uint hash = 2166136261;
            ref ulong bits = ref MemoryMarshal.GetArrayDataReference(_bits);

            for (int i = 0; i < _bits.Length; i++)
            {
                ulong v = Unsafe.Add(ref bits, i);
                hash = (hash ^ (uint)v) * 16777619;
                hash = (hash ^ (uint)(v >> 32)) * 16777619;
            }
            return (int)hash;
        }

        public static bool operator ==(ArchetypeSignature? left, ArchetypeSignature? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(ArchetypeSignature? left, ArchetypeSignature? right) => !(left == right);
    }
}
