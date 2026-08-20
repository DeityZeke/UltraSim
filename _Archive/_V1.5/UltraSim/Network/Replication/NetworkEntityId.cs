using System;

namespace UltraSim.Network
{
    public readonly struct NetworkEntityId : IEquatable<NetworkEntityId>, IComparable<NetworkEntityId>
    {
        public readonly ulong Value;

        public NetworkEntityId(ulong value) => Value = value;

        public int CompareTo(NetworkEntityId other) => Value.CompareTo(other.Value);

        public bool Equals(NetworkEntityId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is NetworkEntityId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(NetworkEntityId left, NetworkEntityId right) => left.Value == right.Value;

        public static bool operator !=(NetworkEntityId left, NetworkEntityId right) => left.Value != right.Value;
    }
}
