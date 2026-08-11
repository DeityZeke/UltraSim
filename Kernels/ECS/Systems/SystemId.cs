using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Stable dense world-local identity assigned during cold registration.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
public readonly struct SystemId : IEquatable<SystemId>, IComparable<SystemId>
{
    private readonly int _value;

    internal SystemId(int value) => _value = value;

    public static SystemId Invalid => new(-1);

    /// <summary>
    /// Creates a cold graph reference to a registration slot. The reference is
    /// validated against the final sealed graph; it is not a runtime lookup.
    /// </summary>
    public static SystemId FromRegistrationIndex(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new SystemId(index);
    }

    public int Value => _value;

    public bool IsValid => _value >= 0;

    public int CompareTo(SystemId other) => _value.CompareTo(other._value);

    public bool Equals(SystemId other) => _value == other._value;

    public override bool Equals(object? obj) => obj is SystemId other && Equals(other);

    public override int GetHashCode() => _value;

    public override string ToString() => IsValid ? $"System({_value})" : "System(Invalid)";

    public static bool operator ==(SystemId left, SystemId right) => left.Equals(right);

    public static bool operator !=(SystemId left, SystemId right) => !left.Equals(right);
}
