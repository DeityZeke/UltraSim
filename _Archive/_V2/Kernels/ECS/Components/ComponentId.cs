using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public readonly struct ComponentId : IEquatable<ComponentId>
{
    // Sentinel used for transition-plan construction and "not a real component id" states.
    public static readonly ComponentId Invalid = new(-1);

    public int Value { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentId(int value)
    {
        Value = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ComponentId other) => Value == other.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is ComponentId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ComponentId left, ComponentId right) => left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ComponentId left, ComponentId right) => !left.Equals(right);

    public override string ToString() => Value.ToString();
}
