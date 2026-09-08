using System.Runtime.CompilerServices;

namespace UltraSim.Network;

/// <summary>All fixed reservations owned by one Network kernel instance.</summary>
public readonly record struct NetworkKernelLimits(
    int MaximumConnections,
    int PacketBufferCount,
    int PacketBufferBytes,
    int InboundRingCapacity,
    int OutboundRingCapacity,
    int MaximumProtocolMessages)
{
    public static NetworkKernelLimits Default => new(
        1_024, 4_096, 1_472, 4_096, 4_096, 256);

    internal void Validate()
    {
        if (MaximumConnections is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(MaximumConnections));
        if (PacketBufferCount is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(PacketBufferCount));
        if (PacketBufferBytes is < 256 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(PacketBufferBytes));
        if (InboundRingCapacity is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(InboundRingCapacity));
        if (OutboundRingCapacity is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(OutboundRingCapacity));
        if (MaximumProtocolMessages is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumProtocolMessages));

        _ = checked((long)PacketBufferCount * PacketBufferBytes);
        _ = checked((long)MaximumConnections * NetworkLaneContracts.Count * sizeof(ulong));
    }

    /// <summary>Returns known fixed-array payload bytes, excluding CLR object headers.</summary>
    public long EstimateReservedPayloadBytes()
    {
        Validate();
        return checked(
            (long)PacketBufferCount * PacketBufferBytes +
            (long)PacketBufferCount * (sizeof(uint) + sizeof(byte) + sizeof(int)) +
            (long)MaximumConnections * (sizeof(uint) + sizeof(byte) + sizeof(uint) + sizeof(int)) +
            (long)MaximumConnections * NetworkLaneContracts.Count * sizeof(ulong) +
            (long)(InboundRingCapacity + OutboundRingCapacity) * Unsafe.SizeOf<PacketBufferToken>());
    }
}
