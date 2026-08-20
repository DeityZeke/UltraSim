using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly uint Index;
        public readonly uint Version;
        public readonly ulong Packed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity(uint index, uint version)
        {
            Index = index;
            Version = version;
            Packed = ((ulong)version << 32) | index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity(ulong packed)
        {
            Packed = packed;
            Index = (uint)packed;
            Version = (uint)(packed >> 32);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid() => Version != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Entity other) => Packed == other.Packed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Entity other && Packed == other.Packed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Packed.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Entity left, Entity right) => left.Packed == right.Packed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Entity left, Entity right) => left.Packed != right.Packed;

        public override string ToString() => $"Entity({Index}:{Version})";

        public static readonly Entity Null = default;
    }
}
