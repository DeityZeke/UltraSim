namespace UltraSim.Network;

/// <summary>Fixed reservations and timing bounds for one secure protocol peer.</summary>
public readonly record struct NetworkProtocolLimits(
    int MaximumReliableMessages,
    int MaximumReceivedMessages,
    int MaximumMessageBytes,
    int MaximumFragmentsPerMessage,
    int SentPacketCapacity,
    int MaximumInboundPacketsPerPump,
    int MaximumPacketsPerPump,
    int MinimumRetransmissionSteps,
    int MaximumRetransmissionSteps,
    int InitialCongestionWindowPackets,
    int MaximumCongestionWindowPackets,
    int MaximumIndependentUnreliableMessages)
{
    public static NetworkProtocolLimits Default => new(
        128, 128, 16_384, 64, 2_048, 64, 64, 2, 128, 4, 128, 128);

    internal void Validate(DeterministicDatagramLimits datagrams)
    {
        datagrams.Validate();
        if (MaximumReliableMessages is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumReliableMessages));
        if (MaximumReceivedMessages is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumReceivedMessages));
        if (MaximumIndependentUnreliableMessages is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumIndependentUnreliableMessages));
        if (MaximumMessageBytes is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(MaximumMessageBytes));
        if (MaximumFragmentsPerMessage is <= 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaximumFragmentsPerMessage));
        if (SentPacketCapacity is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(SentPacketCapacity));
        if (MaximumInboundPacketsPerPump is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumInboundPacketsPerPump));
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
        _ = checked((long)MaximumIndependentUnreliableMessages * MaximumMessageBytes);
        _ = checked((long)MaximumReceivedMessages * MaximumMessageBytes);
        _ = checked((long)NetworkLaneContracts.Count * MaximumMessageBytes);
    }

    /// <summary>
    /// Returns a conservative preconstruction estimate for peer-owned array and
    /// secret payload. CLR object/array headers and opaque cryptography-provider
    /// state are not part of this payload accounting contract.
    /// </summary>
    public long EstimateReservedPayloadBytes(
        DeterministicDatagramLimits datagrams,
        int protocolMessageCount = NetworkLaneContracts.Count)
    {
        Validate(datagrams);
        if (protocolMessageCount <= 0) throw new ArgumentOutOfRangeException(nameof(protocolMessageCount));
        return checked(
            (long)MaximumReliableMessages * MaximumMessageBytes +
            (long)MaximumIndependentUnreliableMessages * MaximumMessageBytes +
            (long)MaximumReceivedMessages * MaximumMessageBytes +
            (long)protocolMessageCount * MaximumMessageBytes +
            (long)NetworkLaneContracts.Count * MaximumMessageBytes +
            (long)MaximumReliableMessages * MaximumFragmentsPerMessage * sizeof(ulong) +
            (long)SentPacketCapacity * NetworkSentPacketMetadata.EstimatedBytes +
            (long)MaximumReliableMessages * (NetworkOutgoingMessageMetadata.EstimatedBytes + sizeof(int)) +
            (long)MaximumIndependentUnreliableMessages *
                (NetworkOutgoingMessageMetadata.EstimatedBytes + sizeof(int)) +
            (long)protocolMessageCount * NetworkUnreliableStreamMetadata.EstimatedBytes +
            (long)NetworkLaneContracts.Count * (NetworkReassemblyMetadata.EstimatedBytes +
                sizeof(ulong) * 2 + sizeof(byte) + sizeof(int)) +
            (long)MaximumReceivedMessages * (NetworkDeliveredMessageMetadata.EstimatedBytes + sizeof(int) * 2) +
            4L * datagrams.MaximumDatagramBytes +
            64L);
    }
}

internal static class NetworkSentPacketMetadata
{
    internal const int EstimatedBytes = 32;
}

internal static class NetworkOutgoingMessageMetadata
{
    internal const int EstimatedBytes = 32;
}

internal static class NetworkUnreliableStreamMetadata
{
    internal const int EstimatedBytes = 56;
}

internal static class NetworkReassemblyMetadata
{
    internal const int EstimatedBytes = 32;
}

internal static class NetworkDeliveredMessageMetadata
{
    internal const int EstimatedBytes = 24;
}

/// <summary>
/// Cold, capacity-scanning evidence of unreliable frames already pending for one
/// peer at one emit opportunity. This is benchmark instrumentation, not a hot
/// scheduling API.
/// </summary>
internal readonly record struct NetworkUnreliableBatchOpportunity(
    int ReadyFrames,
    int PlaintextBytes,
    int GreedyDatagrams);

/// <summary>
/// Opt-in protocol-stage timing. Values are raw <see cref="System.Diagnostics.Stopwatch"/>
/// ticks and deliberately remain outside normal snapshot/hot-path accounting.
/// </summary>
internal readonly record struct NetworkProtocolStageTiming(
    ulong EmitTicks,
    ulong SelectionTicks,
    ulong FrameEncodeTicks,
    ulong SecureSendTicks,
    ulong EndpointSendTicks,
    ulong InboundTicks,
    ulong EndpointReceiveTicks,
    ulong SecureReceiveTicks,
    ulong FrameAdmissionTicks);

/// <summary>
/// Cold protocol diagnostics. <see cref="ReservedBytes"/> is the exact payload
/// of peer-owned managed arrays and directional master-secret copies; it excludes
/// CLR object/array headers and opaque cryptography-provider state.
/// </summary>
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
    ulong DataPacketsSent,
    ulong AcknowledgementPacketsSent,
    ulong KeepAlivePacketsSent,
    ulong EncryptedPlaintextBytes,
    ulong DecryptedPlaintextBytes,
    ulong ReliableFragmentsSent,
    ulong ReliableRetransmittedWireBytes,
    int ReliableQueued,
    int IndependentUnreliableQueued,
    int ReceivedQueued,
    int CongestionWindowPackets,
    int InFlightPackets,
    ulong SmoothedRoundTripSteps,
    ulong RetransmissionTimeoutSteps,
    long ReservedBytes);
