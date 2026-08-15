using System.Runtime.InteropServices;

namespace UltraSim.Client.Network;

/// <summary>Client-local dense projection storage identity.</summary>
/// <remarks>
/// This identity is never sent by the server. Rebinding a local slot to a new
/// canonical replication generation advances its own generation, fencing stale
/// client-side handles independently from network identity.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct ClientEntitySlot : IEquatable<ClientEntitySlot>
{
    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid => Generation != 0;

    public ClientEntitySlot(uint index, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        Index = index;
        Generation = generation;
    }

    public bool Equals(ClientEntitySlot other) => Index == other.Index && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is ClientEntitySlot other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Index, Generation);
    public override string ToString() => IsValid ? $"{Index}:{Generation}" : "invalid";
    public static bool operator ==(ClientEntitySlot left, ClientEntitySlot right) => left.Equals(right);
    public static bool operator !=(ClientEntitySlot left, ClientEntitySlot right) => !left.Equals(right);
}
