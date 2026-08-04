using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Dense world-local identity of one canonical archetype.
/// </summary>
/// <remarks>
/// Value zero is invalid. Archetype IDs are transient runtime routing data and
/// are not persistence identities or stable across worlds.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
public readonly struct ArchetypeId : IEquatable<ArchetypeId>, IComparable<ArchetypeId>
{
    private readonly uint _value;

    internal ArchetypeId(uint value)
    {
        _value = value;
    }

    public static ArchetypeId Invalid => default;

    public uint Value => _value;

    public bool IsValid => _value != 0;

    public int CompareTo(ArchetypeId other) => _value.CompareTo(other._value);

    public bool Equals(ArchetypeId other) => _value == other._value;

    public override bool Equals(object? obj) => obj is ArchetypeId other && Equals(other);

    public override int GetHashCode() => (int)_value;

    public override string ToString() => IsValid ? $"Archetype({_value})" : "Archetype(Invalid)";

    public static bool operator ==(ArchetypeId left, ArchetypeId right) => left.Equals(right);

    public static bool operator !=(ArchetypeId left, ArchetypeId right) => !left.Equals(right);
}
