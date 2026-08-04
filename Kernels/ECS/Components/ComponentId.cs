using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Dense, schema-local component identifier. The value is not durable across schemas.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 2)]
public readonly struct ComponentId : IEquatable<ComponentId>, IComparable<ComponentId>
{
    public static readonly ComponentId Invalid = new(ushort.MaxValue);

    public ComponentId(ushort value)
    {
        Value = value;
    }

    public ushort Value { get; }

    public bool IsValid => Value != ushort.MaxValue;

    public int CompareTo(ComponentId other) => Value.CompareTo(other.Value);

    public bool Equals(ComponentId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is ComponentId other && Equals(other);

    public override int GetHashCode() => Value;

    public override string ToString() => IsValid ? Value.ToString() : "Invalid";

    public static bool operator ==(ComponentId left, ComponentId right) => left.Equals(right);

    public static bool operator !=(ComponentId left, ComponentId right) => !left.Equals(right);
}
