namespace UltraSim.Network;

/// <summary>Fixed reservation owned by one deterministic two-peer datagram topology.</summary>
public readonly record struct DeterministicDatagramLimits(int SlotCapacity, int MaximumDatagramBytes)
{
    public static DeterministicDatagramLimits Default => new(4_096, 1_472);

    internal void Validate()
    {
        if (SlotCapacity is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(SlotCapacity));
        if (MaximumDatagramBytes is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumDatagramBytes));
        _ = checked((long)SlotCapacity * MaximumDatagramBytes * 2L);
    }

    /// <summary>Returns known two-direction payload and metadata bytes, excluding CLR object headers.</summary>
    public long EstimateReservedPayloadBytes()
    {
        Validate();
        return checked(2L * SlotCapacity *
            (MaximumDatagramBytes + DatagramSlotMetadata.EstimatedBytes + sizeof(int)));
    }
}

internal static class DatagramSlotMetadata
{
    internal const int EstimatedBytes = sizeof(int) * 3 + sizeof(ulong) * 2 + sizeof(byte);
}

public enum DatagramSendStatus : byte
{
    /// <summary>
    /// The endpoint accepted ownership of a copied datagram. A queued/batched
    /// endpoint may not yet have submitted it to the operating-system socket;
    /// this is a transport-admission result, not proof of wire delivery.
    /// </summary>
    Accepted = 0,
    DroppedByImpairment = 1,
    CapacityExceeded = 2,
    DatagramTooLarge = 3,
}
