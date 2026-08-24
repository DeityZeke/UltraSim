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
    int MaximumIndependentUnreliableMessages,
    int ReliablePayloadByteCapacity = 262_144,
    int ReceivedPayloadByteCapacity = 262_144,
    int MaximumUnreliableFramesPerDatagram = 1)
{
    internal const int MaximumUnreliableFramesPerPumpBound = 262_144;

    public static NetworkProtocolLimits Default => new(
        128, 128, 16_384, 64, 2_048, 64, 64, 2, 128, 4, 128, 128);

    /// <summary>
    /// Explicit bounded-work contract for the maximum number of unreliable frames
    /// a send pump may select before endpoint submission.
    /// </summary>
    public int MaximumUnreliableFramesPerPump => checked(
        MaximumPacketsPerPump * MaximumUnreliableFramesPerDatagram);

    internal void Validate(DeterministicDatagramLimits datagrams)
    {
        datagrams.Validate();
        if (MaximumReliableMessages is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumReliableMessages));
        if (MaximumReceivedMessages is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumReceivedMessages));
        if (MaximumIndependentUnreliableMessages is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumIndependentUnreliableMessages));
        if (MaximumUnreliableFramesPerDatagram is <= 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaximumUnreliableFramesPerDatagram));
        if (MaximumMessageBytes is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(MaximumMessageBytes));
        if (ReliablePayloadByteCapacity < MaximumMessageBytes)
            throw new ArgumentOutOfRangeException(nameof(ReliablePayloadByteCapacity));
        if (ReceivedPayloadByteCapacity < MaximumMessageBytes)
            throw new ArgumentOutOfRangeException(nameof(ReceivedPayloadByteCapacity));
        if (MaximumFragmentsPerMessage is <= 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaximumFragmentsPerMessage));
        if (SentPacketCapacity is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(SentPacketCapacity));
        if (MaximumInboundPacketsPerPump is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumInboundPacketsPerPump));
        if (MaximumPacketsPerPump is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumPacketsPerPump));
        if ((long)MaximumPacketsPerPump * MaximumUnreliableFramesPerDatagram >
            MaximumUnreliableFramesPerPumpBound)
            throw new ArgumentOutOfRangeException(nameof(MaximumUnreliableFramesPerDatagram),
                "The packet-pump and aggregate-frame limits exceed the bounded-work contract.");
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
            ReliablePayloadByteCapacity +
            (long)MaximumIndependentUnreliableMessages * MaximumMessageBytes +
            ReceivedPayloadByteCapacity +
            (long)protocolMessageCount * MaximumMessageBytes +
            (long)NetworkLaneContracts.Count * MaximumMessageBytes +
            (long)MaximumReliableMessages * MaximumFragmentsPerMessage * sizeof(ulong) +
            (long)SentPacketCapacity * NetworkSentPacketMetadata.EstimatedBytes +
            NetworkSentPacketIndexMetadata.EstimateBytes(
                SentPacketCapacity, MaximumReliableMessages, MaximumFragmentsPerMessage) +
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

    /// <summary>
    /// Returns a descriptor- and lane-aware preconstruction estimate. Unlike the
    /// count-only overload, this reflects the payload shapes the supplied protocol
    /// can actually admit, including the nonfragmenting one-datagram ceiling.
    /// </summary>
    public long EstimateReservedPayloadBytes(
        DeterministicDatagramLimits datagrams,
        NetworkProtocolManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Validate(datagrams);
        int fragmentPayload = NetworkSecureDatagramFraming.MaximumPlaintextBytes(
            datagrams.MaximumDatagramBytes) - NetworkProtocolDataHeader.EncodedBytes;
        NetworkPayloadReservationLayout layout = NetworkPayloadReservationLayout.Create(
            manifest, this, fragmentPayload);
        return checked(
            Math.Min((long)MaximumReliableMessages * layout.ReliableStride,
                ReliablePayloadByteCapacity) +
            (long)MaximumIndependentUnreliableMessages * layout.IndependentStride +
            Math.Min((long)MaximumReceivedMessages * layout.ReceivedStride,
                ReceivedPayloadByteCapacity) +
            layout.UnreliableOffsets[^1] +
            layout.ReassemblyOffsets[^1] +
            (long)MaximumReliableMessages * MaximumFragmentsPerMessage * sizeof(ulong) +
            (long)SentPacketCapacity * NetworkSentPacketMetadata.EstimatedBytes +
            NetworkSentPacketIndexMetadata.EstimateBytes(
                SentPacketCapacity, MaximumReliableMessages, MaximumFragmentsPerMessage) +
            (long)MaximumReliableMessages * (NetworkOutgoingMessageMetadata.EstimatedBytes + sizeof(int)) +
            (long)MaximumIndependentUnreliableMessages *
                (NetworkOutgoingMessageMetadata.EstimatedBytes + sizeof(int)) +
            (long)manifest.Count * NetworkUnreliableStreamMetadata.EstimatedBytes +
            (long)(manifest.Count + 1) * sizeof(int) +
            (long)NetworkLaneContracts.Count * (NetworkReassemblyMetadata.EstimatedBytes +
                sizeof(ulong) * 2 + sizeof(byte) + sizeof(int)) +
            (long)(NetworkLaneContracts.Count + 1) * sizeof(int) +
            (long)MaximumReceivedMessages * (NetworkDeliveredMessageMetadata.EstimatedBytes + sizeof(int) * 2) +
            4L * datagrams.MaximumDatagramBytes +
            64L);
    }
}

internal readonly record struct NetworkPayloadReservationLayout(
    int ReliableStride,
    int IndependentStride,
    int ReceivedStride,
    int[] UnreliableOffsets,
    int[] ReassemblyOffsets)
{
    internal static NetworkPayloadReservationLayout Create(
        NetworkProtocolManifest manifest,
        NetworkProtocolLimits limits,
        int fragmentPayloadBytes)
    {
        int reliable = 0;
        int independent = 0;
        int received = 0;
        int[] unreliable = new int[checked(manifest.Count + 1)];
        int[] reassemblyByLane = new int[NetworkLaneContracts.Count];
        ReadOnlySpan<NetworkMessageDescriptor> messages = manifest.Messages;
        for (int index = 0; index < messages.Length; index++)
        {
            NetworkMessageDescriptor descriptor = messages[index];
            NetworkLaneContract lane = NetworkLaneContracts.Get(descriptor.Lane);
            long representable = lane.AllowsFragmentation
                ? checked((long)fragmentPayloadBytes * limits.MaximumFragmentsPerMessage)
                : fragmentPayloadBytes;
            int admitted = checked((int)Math.Min(
                Math.Min((long)limits.MaximumMessageBytes, descriptor.MaximumEncodedBytes), representable));
            received = Math.Max(received, admitted);
            unreliable[index + 1] = unreliable[index];
            if (lane.Delivery == NetworkDeliveryGuarantee.Reliable)
            {
                reliable = Math.Max(reliable, admitted);
                int laneIndex = (int)descriptor.Lane;
                reassemblyByLane[laneIndex] = Math.Max(reassemblyByLane[laneIndex], admitted);
            }
            else if (lane.Retention == NetworkRetentionPolicy.IndependentState)
            {
                independent = Math.Max(independent, admitted);
            }
            else
            {
                unreliable[index + 1] = checked(unreliable[index + 1] + admitted);
            }
        }

        int[] reassembly = new int[NetworkLaneContracts.Count + 1];
        for (int index = 0; index < reassemblyByLane.Length; index++)
            reassembly[index + 1] = checked(reassembly[index] + reassemblyByLane[index]);
        return new(reliable, independent, received, unreliable, reassembly);
    }
}

internal static class NetworkSentPacketMetadata
{
    internal const int EstimatedBytes = 32;
}

internal static class NetworkSentPacketIndexMetadata
{
    internal static long EstimateBytes(
        int sentPacketCapacity,
        int maximumReliableMessages,
        int maximumFragmentsPerMessage)
    {
        int mapCapacity = checked((int)System.Numerics.BitOperations.RoundUpToPowerOf2(
            checked((uint)sentPacketCapacity * 2U)));
        return checked(
            (long)sentPacketCapacity * sizeof(int) +
            (long)maximumReliableMessages * maximumFragmentsPerMessage * sizeof(int) +
            (long)mapCapacity * (sizeof(ulong) + sizeof(int)));
    }
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
    int GreedyDatagrams,
    int SameLaneGreedyDatagrams);

/// <summary>
/// Opt-in protocol-stage timing. Values are raw <see cref="System.Diagnostics.Stopwatch"/>
/// ticks and deliberately remain outside normal snapshot/hot-path accounting.
/// </summary>
internal readonly record struct NetworkProtocolStageTiming(
    ulong EmitTicks,
    ulong EmitCalls,
    ulong IdleEmitCalls,
    ulong SelectionTicks,
    ulong FrameEncodeTicks,
    ulong SecureSendTicks,
    ulong EndpointSendTicks,
    ulong InboundTicks,
    ulong InboundCalls,
    ulong EmptyInboundCalls,
    ulong InboundDatagrams,
    ulong EndpointReceiveTicks,
    ulong SecureReceiveTicks,
    ulong FrameAdmissionTicks,
    ulong ReliableCompactionTicks,
    ulong ReceivedCompactionTicks,
    ulong OutboundUnreliableFrames,
    ulong InboundUnreliableFrames,
    ulong LaneSelectionPasses,
    ulong ReliableSlotProbes,
    ulong IndependentSlotProbes,
    ulong UnreliableSlotProbes,
    ulong ExcludedSelectionProbes);

/// <summary>
/// Cold lifetime counters for the opt-in same-peer unreliable Data aggregate.
/// Histogram fields count secure Data datagrams by embedded frame count; the final
/// bucket is eight or more so the snapshot remains allocation-free.
/// </summary>
public readonly record struct NetworkUnreliableAggregationSnapshot(
    int ConfiguredMaximumFramesPerDatagram,
    ulong SentFrames,
    ulong SentDatagrams,
    ulong ReceivedFrames,
    ulong ReceivedDatagrams,
    ulong SentDatagramsWithTwoOrMoreFrames,
    ulong ReceivedDatagramsWithTwoOrMoreFrames,
    ulong SentHistogramOne,
    ulong SentHistogramTwo,
    ulong SentHistogramThree,
    ulong SentHistogramFour,
    ulong SentHistogramFive,
    ulong SentHistogramSix,
    ulong SentHistogramSeven,
    ulong SentHistogramEightOrMore,
    ulong ReceivedHistogramOne,
    ulong ReceivedHistogramTwo,
    ulong ReceivedHistogramThree,
    ulong ReceivedHistogramFour,
    ulong ReceivedHistogramFive,
    ulong ReceivedHistogramSix,
    ulong ReceivedHistogramSeven,
    ulong ReceivedHistogramEightOrMore,
    ulong SentPlaintextBytes,
    ulong ReceivedPlaintextBytes,
    int MaximumSentFramesPerDatagram,
    int MaximumReceivedFramesPerDatagram,
    int MaximumSentPlaintextBytes,
    int MaximumReceivedPlaintextBytes);

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
    long ReservedBytes,
    NetworkRetainedPayloadSnapshot RetainedPayloads,
    NetworkUnreliableAggregationSnapshot UnreliableAggregation);

/// <summary>
/// Exact cold diagnostics for payload bytes retained by one protocol peer. Capacities
/// describe owned payload arrays, not CLR array headers. High-water values are monotonic
/// for the peer lifetime; a zero byte-capacity failure count is distinct from an
/// unmeasured value. Compaction volume counts bytes physically copied because
/// their retained range moved; deep timing remains opt-in through protocol-stage
/// diagnostics.
/// </summary>
public readonly record struct NetworkRetainedPayloadSnapshot(
    int ReliableBytes,
    int ReliableHighWaterBytes,
    int ReliableCapacityBytes,
    int IndependentBytes,
    int IndependentHighWaterBytes,
    int IndependentCapacityBytes,
    int UnreliableStreamBytes,
    int UnreliableStreamHighWaterBytes,
    int UnreliableStreamCapacityBytes,
    int ReassemblyBytes,
    int ReassemblyHighWaterBytes,
    int ReassemblyCapacityBytes,
    int ReceivedBytes,
    int ReceivedHighWaterBytes,
    int ReceivedCapacityBytes,
    int LargestRetainedMessageBytes,
    ulong ByteCapacityFailures,
    ulong ReliableCompactions,
    ulong ReliableCompactionBytesMoved,
    int ReliableMaximumBytesMovedPerCompaction,
    ulong ReceivedCompactions,
    ulong ReceivedCompactionBytesMoved,
    int ReceivedMaximumBytesMovedPerCompaction)
{
    public long TotalBytes => checked(
        (long)ReliableBytes + IndependentBytes + UnreliableStreamBytes + ReassemblyBytes + ReceivedBytes);

    public long TotalHighWaterBytes => checked(
        (long)ReliableHighWaterBytes + IndependentHighWaterBytes + UnreliableStreamHighWaterBytes +
        ReassemblyHighWaterBytes + ReceivedHighWaterBytes);

    public long TotalCapacityBytes => checked(
        (long)ReliableCapacityBytes + IndependentCapacityBytes + UnreliableStreamCapacityBytes +
        ReassemblyCapacityBytes + ReceivedCapacityBytes);
}
