using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public readonly struct PrefabId : IEquatable<PrefabId>
    {
        public readonly int Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PrefabId(int value) => Value = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(PrefabId other) => Value == other.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is PrefabId other && Value == other.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(PrefabId left, PrefabId right) => left.Value == right.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(PrefabId left, PrefabId right) => left.Value != right.Value;

        public override string ToString() => Value.ToString();

        public static readonly PrefabId Invalid = new(-1);
    }
}
