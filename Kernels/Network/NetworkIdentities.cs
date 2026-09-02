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
