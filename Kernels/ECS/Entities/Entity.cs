using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public readonly struct Entity : IEquatable<Entity>
{
    public static readonly Entity Null = default;
    private readonly ulong _packed;

    public ulong Packed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _packed;
    }

    public uint Index
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (uint)Packed;
    }

    public uint Version
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (uint)(Packed >> 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity(uint index, uint version)
    {
        _packed = ((ulong)version << 32) | index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity(ulong packed)
    {
        _packed = packed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid() => Version != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Entity other) => Packed == other.Packed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => Packed.GetHashCode();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);

    public override string ToString() => $"Entity({Index}:{Version})";
}
