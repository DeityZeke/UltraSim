using System.Runtime.InteropServices;

namespace UltraSim.Network;

[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct SecurityEpoch : IEquatable<SecurityEpoch>, IComparable<SecurityEpoch>
{
    public uint Value { get; }
    public bool IsValid => Value != 0;
    public SecurityEpoch(uint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }
    public bool TryNext(out SecurityEpoch next)
    {
        if (Value == uint.MaxValue) { next = default; return false; }
        next = new SecurityEpoch(Value + 1);
        return true;
    }
    public bool Equals(SecurityEpoch other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is SecurityEpoch other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(SecurityEpoch other) => Value.CompareTo(other.Value);
    public static bool operator ==(SecurityEpoch left, SecurityEpoch right) => left.Equals(right);
    public static bool operator !=(SecurityEpoch left, SecurityEpoch right) => !left.Equals(right);
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct PacketNumber : IEquatable<PacketNumber>, IComparable<PacketNumber>
{
    public ulong Value { get; }
    public PacketNumber(ulong value) => Value = value;
    public bool TryNext(out PacketNumber next)
    {
        if (Value == ulong.MaxValue) { next = default; return false; }
        next = new PacketNumber(Value + 1);
        return true;
    }
    public bool Equals(PacketNumber other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is PacketNumber other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(PacketNumber other) => Value.CompareTo(other.Value);
    public static bool operator ==(PacketNumber left, PacketNumber right) => left.Equals(right);
    public static bool operator !=(PacketNumber left, PacketNumber right) => !left.Equals(right);
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct LaneSequence : IEquatable<LaneSequence>, IComparable<LaneSequence>
{
    public ulong Value { get; }
    public LaneSequence(ulong value) => Value = value;
    public bool TryNext(out LaneSequence next)
    {
        if (Value == ulong.MaxValue) { next = default; return false; }
        next = new LaneSequence(Value + 1);
        return true;
    }
    public bool Equals(LaneSequence other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is LaneSequence other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(LaneSequence other) => Value.CompareTo(other.Value);
    public static bool operator ==(LaneSequence left, LaneSequence right) => left.Equals(right);
    public static bool operator !=(LaneSequence left, LaneSequence right) => !left.Equals(right);
}

/// <summary>Generation-protected ownership token for one fixed packet buffer slot.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct PacketBufferToken : IEquatable<PacketBufferToken>
{
    public uint Slot { get; }
    public uint Generation { get; }
    public bool IsValid => Generation != 0;
    public PacketBufferToken(uint slot, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        Slot = slot;
        Generation = generation;
    }
    public bool Equals(PacketBufferToken other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is PacketBufferToken other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(PacketBufferToken left, PacketBufferToken right) => left.Equals(right);
    public static bool operator !=(PacketBufferToken left, PacketBufferToken right) => !left.Equals(right);
}
