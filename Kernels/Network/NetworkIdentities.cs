using System.Runtime.InteropServices;

namespace UltraSim.Network;

/// <summary>
/// Identifies one live transport connection slot and its reuse generation.
/// </summary>
/// <remarks>
/// The server creates this identity. It is neither client-chosen nor durable and
/// must not be used as an account, participant, persistent-entity, or replication
/// identity. Reusing a slot advances its generation so retained stale identifiers
/// do not authorize a later connection.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct ConnectionId : IEquatable<ConnectionId>, IComparable<ConnectionId>
{
    /// <summary>Gets the connection-table slot.</summary>
    public uint Slot { get; }

    /// <summary>Gets the nonzero reuse generation.</summary>
    public uint Generation { get; }

    /// <summary>Gets whether the identifier names a live-capable generation.</summary>
    public bool IsValid => Generation != 0;

    /// <summary>Creates a server-owned connection identifier.</summary>
    /// <param name="slot">The bounded connection-table slot.</param>
    /// <param name="generation">The nonzero reuse generation.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="generation"/> is zero.</exception>
    public ConnectionId(uint slot, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        Slot = slot;
        Generation = generation;
    }

    /// <inheritdoc />
    public bool Equals(ConnectionId other) => Slot == other.Slot && Generation == other.Generation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ConnectionId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);

    /// <inheritdoc />
    public int CompareTo(ConnectionId other)
    {
        int slot = Slot.CompareTo(other.Slot);
        return slot != 0 ? slot : Generation.CompareTo(other.Generation);
    }

    /// <inheritdoc />
    public override string ToString() => IsValid ? $"{Slot}:{Generation}" : "invalid";

    public static bool operator ==(ConnectionId left, ConnectionId right) => left.Equals(right);
    public static bool operator !=(ConnectionId left, ConnectionId right) => !left.Equals(right);
}

/// <summary>
/// Identifies canonical replicated state within one <see cref="StateEpoch"/>.
/// </summary>
/// <remarks>
/// This identity is connection-independent: every recipient refers to the same
/// canonical replicated entity during an epoch. A future measured wire-alias
/// optimization may encode it differently per connection without changing this
/// identity. It is not an ECS entity handle or a durable persistent identity.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct ReplicationEntityId : IEquatable<ReplicationEntityId>, IComparable<ReplicationEntityId>
{
    /// <summary>Gets the canonical replication-table index.</summary>
    public uint Index { get; }

    /// <summary>Gets the nonzero reuse generation.</summary>
    public uint Generation { get; }

    /// <summary>Gets whether the identity is valid.</summary>
    public bool IsValid => Generation != 0;

    /// <summary>Creates a canonical replication identity.</summary>
    /// <param name="index">The bounded canonical projection index.</param>
    /// <param name="generation">The nonzero reuse generation.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="generation"/> is zero.</exception>
    public ReplicationEntityId(uint index, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        Index = index;
        Generation = generation;
    }

    /// <inheritdoc />
    public bool Equals(ReplicationEntityId other) => Index == other.Index && Generation == other.Generation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ReplicationEntityId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Index, Generation);

    /// <inheritdoc />
    public int CompareTo(ReplicationEntityId other)
    {
        int index = Index.CompareTo(other.Index);
        return index != 0 ? index : Generation.CompareTo(other.Generation);
    }

    /// <inheritdoc />
    public override string ToString() => IsValid ? $"{Index}:{Generation}" : "invalid";

    public static bool operator ==(ReplicationEntityId left, ReplicationEntityId right) => left.Equals(right);
    public static bool operator !=(ReplicationEntityId left, ReplicationEntityId right) => !left.Equals(right);
}

/// <summary>
/// Identifies one coherent canonical projection epoch.
/// </summary>
/// <remarks>
/// Generation replacement advances the epoch and invalidates replication
/// identities and baselines from the previous epoch. Connection lifetime is
/// independent: a healthy connection may remain while its projection epoch changes.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct StateEpoch : IEquatable<StateEpoch>, IComparable<StateEpoch>
{
    /// <summary>Gets the nonzero epoch value.</summary>
    public ulong Value { get; }

    /// <summary>Gets whether the epoch is valid.</summary>
    public bool IsValid => Value != 0;

    /// <summary>Creates a projection epoch.</summary>
    /// <param name="value">The nonzero monotonically assigned epoch value.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is zero.</exception>
    public StateEpoch(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    /// <inheritdoc />
    public bool Equals(StateEpoch other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StateEpoch other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(StateEpoch other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => IsValid ? Value.ToString() : "invalid";

    public static bool operator ==(StateEpoch left, StateEpoch right) => left.Equals(right);
    public static bool operator !=(StateEpoch left, StateEpoch right) => !left.Equals(right);
}
