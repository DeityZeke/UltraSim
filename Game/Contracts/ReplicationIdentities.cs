using System.Runtime.InteropServices;

namespace UltraSim.Game.Replication;

/// <summary>Identifies canonical replicated state within one <see cref="StateEpoch"/>.</summary>
/// <remarks>
/// This Game identity is connection-independent. It is not an ECS entity handle,
/// a durable persistent identity, or a Network connection identity.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct ReplicationEntityId : IEquatable<ReplicationEntityId>, IComparable<ReplicationEntityId>
{
    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid => Generation != 0;

    public ReplicationEntityId(uint index, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        Index = index;
        Generation = generation;
    }

    public bool Equals(ReplicationEntityId other) => Index == other.Index && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is ReplicationEntityId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Index, Generation);
    public int CompareTo(ReplicationEntityId other)
    {
        int index = Index.CompareTo(other.Index);
        return index != 0 ? index : Generation.CompareTo(other.Generation);
    }
    public override string ToString() => IsValid ? $"{Index}:{Generation}" : "invalid";
    public static bool operator ==(ReplicationEntityId left, ReplicationEntityId right) => left.Equals(right);
    public static bool operator !=(ReplicationEntityId left, ReplicationEntityId right) => !left.Equals(right);
}

/// <summary>Identifies one coherent canonical Game projection epoch.</summary>
/// <remarks>
/// Generation replacement advances this Game-owned lineage independently from
/// transport connection lifetime.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct StateEpoch : IEquatable<StateEpoch>, IComparable<StateEpoch>
{
    public ulong Value { get; }
    public bool IsValid => Value != 0;

    public StateEpoch(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public bool Equals(StateEpoch other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is StateEpoch other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(StateEpoch other) => Value.CompareTo(other.Value);
    public override string ToString() => IsValid ? Value.ToString() : "invalid";
    public static bool operator ==(StateEpoch left, StateEpoch right) => left.Equals(right);
    public static bool operator !=(StateEpoch left, StateEpoch right) => !left.Equals(right);
}
