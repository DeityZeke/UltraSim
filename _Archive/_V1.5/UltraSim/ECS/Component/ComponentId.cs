using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public readonly struct ComponentId : IEquatable<ComponentId>
    {
        public readonly int Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentId(int value) => Value = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ComponentId other) => Value == other.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is ComponentId other && Value == other.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(ComponentId left, ComponentId right) => left.Value == right.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(ComponentId left, ComponentId right) => left.Value != right.Value;

        public override string ToString() => Value.ToString();

        public static readonly ComponentId Invalid = new(-1);
    }
}
