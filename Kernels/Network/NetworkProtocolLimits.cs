namespace UltraSim.Network;

/// <summary>Fixed reservations and timing bounds for one secure protocol peer.</summary>
public readonly record struct NetworkProtocolLimits(
    int MaximumReliableMessages,
    int MaximumReceivedMessages,
    int MaximumMessageBytes,
    int MaximumFragmentsPerMessage,
    int SentPacketCapacity,
    int MaximumPacketsPerPump,
    int MinimumRetransmissionSteps,
    int MaximumRetransmissionSteps,
    int InitialCongestionWindowPackets,
    int MaximumCongestionWindowPackets)
{
    public static NetworkProtocolLimits Default => new(
        128, 128, 16_384, 64, 2_048, 64, 2, 128, 4, 128);

    internal void Validate(DeterministicDatagramLimits datagrams)
    {
        datagrams.Validate();
        if (MaximumReliableMessages is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumReliableMessages));
        if (MaximumReceivedMessages is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumReceivedMessages));
        if (MaximumMessageBytes is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(MaximumMessageBytes));
        if (MaximumFragmentsPerMessage is <= 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaximumFragmentsPerMessage));
        if (SentPacketCapacity is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(SentPacketCapacity));
        if (MaximumPacketsPerPump is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumPacketsPerPump));
        if (MinimumRetransmissionSteps <= 0 || MaximumRetransmissionSteps < MinimumRetransmissionSteps)
            throw new ArgumentOutOfRangeException(nameof(MinimumRetransmissionSteps));
        if (InitialCongestionWindowPackets <= 0 ||
            MaximumCongestionWindowPackets < InitialCongestionWindowPackets ||
            MaximumCongestionWindowPackets > SentPacketCapacity)
            throw new ArgumentOutOfRangeException(nameof(InitialCongestionWindowPackets));
        if (NetworkSecureDatagramFraming.MaximumPlaintextBytes(datagrams.MaximumDatagramBytes) <=
            NetworkProtocolDataHeader.EncodedBytes)
            throw new ArgumentOutOfRangeException(nameof(datagrams));

        _ = checked((long)MaximumReliableMessages * MaximumMessageBytes);
        _ = checked((long)MaximumReceivedMessages * MaximumMessageBytes);
        _ = checked((long)NetworkLaneContracts.Count * MaximumMessageBytes);
    }

    public long EstimateReservedPayloadBytes(DeterministicDatagramLimits datagrams)
    {
        Validate(datagrams);
        return checked(
            (long)MaximumReliableMessages * MaximumMessageBytes +
            (long)MaximumReceivedMessages * MaximumMessageBytes +
            2L * NetworkLaneContracts.Count * MaximumMessageBytes +
            (long)MaximumReliableMessages * MaximumFragmentsPerMessage * sizeof(ulong) +
            (long)SentPacketCapacity * NetworkSentPacketMetadata.EstimatedBytes +
            4L * datagrams.MaximumDatagramBytes);
    }
}

internal static class NetworkSentPacketMetadata
{
    internal const int EstimatedBytes = sizeof(ulong) * 2 + sizeof(int) * 2 + sizeof(byte);
}

public readonly record struct NetworkProtocolSnapshot(
    bool Established,
    SecurityEpoch SendSecurityEpoch,
    SecurityEpoch ReceiveSecurityEpoch,
    ulong SentPackets,
    ulong ReceivedPackets,
    ulong AuthenticationFailures,
    ulong ReplayRejections,
    ulong ReliableRetransmissions,
    ulong ReliableAcknowledgements,
    ulong SequencedDrops,
    ulong CapacityDrops,
    int ReliableQueued,
    int ReceivedQueued,
    int CongestionWindowPackets,
    int InFlightPackets,
    ulong SmoothedRoundTripSteps,
    ulong RetransmissionTimeoutSteps,
    long ReservedBytes);

