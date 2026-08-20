using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public readonly struct SystemId : IEquatable<SystemId>
{
    public static readonly SystemId Invalid = new(-1);

    public int Value { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemId(int value)
    {
        Value = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(SystemId other) => Value == other.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is SystemId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(SystemId left, SystemId right) => left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(SystemId left, SystemId right) => !left.Equals(right);

    public override string ToString() => Value.ToString();
}
