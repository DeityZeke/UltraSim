using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public readonly struct SystemId : IEquatable<SystemId>
    {
        public readonly int Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SystemId(int value) => Value = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(SystemId other) => Value == other.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is SystemId other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(SystemId left, SystemId right) => left.Value == right.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(SystemId left, SystemId right) => left.Value != right.Value;

        public override string ToString() => Value.ToString();

        public static readonly SystemId Invalid = new(-1);
    }
}
