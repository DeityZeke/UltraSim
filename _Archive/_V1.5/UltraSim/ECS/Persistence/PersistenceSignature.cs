using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Canonical ordered set of persistent component ids used to group entities for persistence.
    /// This is a persistence-only signature; it is not the runtime archetype id/signature.
    /// </summary>
    public sealed class PersistenceSignature : IEquatable<PersistenceSignature>
    {
        private readonly uint[] _persistenceIds;
        private readonly int _hashCode;

        public int ComponentCount => _persistenceIds.Length;
        public ReadOnlySpan<uint> PersistenceIds => _persistenceIds;

        public PersistenceSignature(ReadOnlySpan<uint> persistenceIds)
        {
            _persistenceIds = persistenceIds.ToArray();
            _hashCode = ComputeHash(_persistenceIds);
        }

        public bool Equals(PersistenceSignature? other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            if (_hashCode != other._hashCode)
                return false;
            if (_persistenceIds.Length != other._persistenceIds.Length)
                return false;

            ref uint a = ref MemoryMarshal.GetArrayDataReference(_persistenceIds);
            ref uint b = ref MemoryMarshal.GetArrayDataReference(other._persistenceIds);
            for (int i = 0; i < _persistenceIds.Length; i++)
            {
                if (Unsafe.Add(ref a, i) != Unsafe.Add(ref b, i))
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is PersistenceSignature other && Equals(other);
        public override int GetHashCode() => _hashCode;

        public static bool operator ==(PersistenceSignature? left, PersistenceSignature? right)
        {
            if (left is null)
                return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(PersistenceSignature? left, PersistenceSignature? right) => !(left == right);

        private static int ComputeHash(ReadOnlySpan<uint> ids)
        {
            uint hash = 2166136261;
            for (int i = 0; i < ids.Length; i++)
                hash = (hash ^ ids[i]) * 16777619;
            return unchecked((int)hash);
        }
    }
}
