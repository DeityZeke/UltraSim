namespace UltraSim.Network;

/// <summary>Physical capability scope for already-ready unreliable Data frames.</summary>
public enum NetworkUnreliableAggregationScope : byte
{
    SameLane = 0,
    CrossLane = 1,
}

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
    int MaximumUnreliableFramesPerDatagram = 1,
    NetworkUnreliableAggregationScope UnreliableAggregationScope =
        NetworkUnreliableAggregationScope.CrossLane)
{
    internal const int MaximumUnreliableFramesPerPumpBound = 262_144;

    public static NetworkProtocolLimits Default => new(
        128, 128, 16_384, 64, 2_048, 64, 64, 2, 128, 4, 128, 128,
        MaximumUnreliableFramesPerDatagram: 2,
        UnreliableAggregationScope: NetworkUnreliableAggregationScope.CrossLane);

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
        if (!Enum.IsDefined(UnreliableAggregationScope))
            throw new ArgumentOutOfRangeException(nameof(UnreliableAggregationScope));
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
            NetworkReadySelectionMetadata.EstimateBytes(
                MaximumReliableMessages, MaximumIndependentUnreliableMessages) +
            (long)protocolMessageCount * NetworkUnreliableStreamMetadata.EstimatedBytes +
            (long)(protocolMessageCount + NetworkLaneContracts.Count) *
                NetworkTrafficCounterMetadata.EstimatedBytes +
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
            NetworkReadySelectionMetadata.EstimateBytes(
                MaximumReliableMessages, MaximumIndependentUnreliableMessages) +
            (long)manifest.Count * NetworkUnreliableStreamMetadata.EstimatedBytes +
            (long)(manifest.Count + NetworkLaneContracts.Count) *
                NetworkTrafficCounterMetadata.EstimatedBytes +
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
    // Includes the manifest slot retained for allocation-free per-message
    // accounting. The runtime record is eight-byte aligned.
    internal const int EstimatedBytes = 40;
}

internal static class NetworkReadySelectionMetadata
{
    internal static long EstimateBytes(
        int maximumReliableMessages,
        int maximumIndependentMessages)
    {
        int independentWords = checked((maximumIndependentMessages + 63) / 64);
        return checked(
            (long)maximumReliableMessages * sizeof(int) +
            (long)NetworkLaneContracts.Count * sizeof(int) * 3L +
            (long)NetworkLaneContracts.Count * independentWords * sizeof(ulong));
    }
}

internal static class NetworkUnreliableStreamMetadata
{
    internal const int EstimatedBytes = 56;
}

internal static class NetworkTrafficCounterMetadata
{
    internal const int EstimatedBytes = sizeof(ulong) * 8;
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
    int SameLaneGreedyDatagrams,
    int CapTwoGreedyDatagrams,
    int CapFourGreedyDatagrams,
    int CapEightGreedyDatagrams,
    int SameLaneCapTwoGreedyDatagrams,
    int SameLaneCapFourGreedyDatagrams,
    int SameLaneCapEightGreedyDatagrams,
    uint LaneMask,
    int InputFrames,
    int OwnerStateFrames,
    int WorldStateFrames,
    int DisposableFrames,
    int OtherFrames);

/// <summary>
/// Opt-in protocol-stage timing. Values are raw <see cref="System.Diagnostics.Stopwatch"/>
/// ticks and deliberately remain outside normal snapshot/hot-path accounting.
/// </summary>
public readonly record struct NetworkProtocolStageTiming(
    NetworkTelemetryLevel Level,
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
    ulong ExcludedSelectionProbes,
    ulong EmptySelectionAttempts,
    ulong ReadySelections)
{
    public static NetworkProtocolStageTiming Delta(
        in NetworkProtocolStageTiming start,
        in NetworkProtocolStageTiming end)
    {
        if (start.Level != end.Level)
            throw new ArgumentException("Protocol timing snapshots use different telemetry levels.");
        return new(end.Level,
            Difference(start.EmitTicks, end.EmitTicks),
            Difference(start.EmitCalls, end.EmitCalls),
            Difference(start.IdleEmitCalls, end.IdleEmitCalls),
            Difference(start.SelectionTicks, end.SelectionTicks),
            Difference(start.FrameEncodeTicks, end.FrameEncodeTicks),
            Difference(start.SecureSendTicks, end.SecureSendTicks),
            Difference(start.EndpointSendTicks, end.EndpointSendTicks),
            Difference(start.InboundTicks, end.InboundTicks),
            Difference(start.InboundCalls, end.InboundCalls),
            Difference(start.EmptyInboundCalls, end.EmptyInboundCalls),
            Difference(start.InboundDatagrams, end.InboundDatagrams),
            Difference(start.EndpointReceiveTicks, end.EndpointReceiveTicks),
            Difference(start.SecureReceiveTicks, end.SecureReceiveTicks),
            Difference(start.FrameAdmissionTicks, end.FrameAdmissionTicks),
            Difference(start.ReliableCompactionTicks, end.ReliableCompactionTicks),
            Difference(start.ReceivedCompactionTicks, end.ReceivedCompactionTicks),
            Difference(start.OutboundUnreliableFrames, end.OutboundUnreliableFrames),
            Difference(start.InboundUnreliableFrames, end.InboundUnreliableFrames),
            Difference(start.LaneSelectionPasses, end.LaneSelectionPasses),
            Difference(start.ReliableSlotProbes, end.ReliableSlotProbes),
            Difference(start.IndependentSlotProbes, end.IndependentSlotProbes),
            Difference(start.UnreliableSlotProbes, end.UnreliableSlotProbes),
            Difference(start.ExcludedSelectionProbes, end.ExcludedSelectionProbes),
            Difference(start.EmptySelectionAttempts, end.EmptySelectionAttempts),
            Difference(start.ReadySelections, end.ReadySelections));
    }

    public NetworkProtocolStageTiming Add(in NetworkProtocolStageTiming other)
    {
        if (Level != other.Level)
            throw new ArgumentException("Protocol timing snapshots use different telemetry levels.");
        return new(Level,
            Add(EmitTicks, other.EmitTicks), Add(EmitCalls, other.EmitCalls),
            Add(IdleEmitCalls, other.IdleEmitCalls), Add(SelectionTicks, other.SelectionTicks),
            Add(FrameEncodeTicks, other.FrameEncodeTicks), Add(SecureSendTicks, other.SecureSendTicks),
            Add(EndpointSendTicks, other.EndpointSendTicks), Add(InboundTicks, other.InboundTicks),
            Add(InboundCalls, other.InboundCalls), Add(EmptyInboundCalls, other.EmptyInboundCalls),
            Add(InboundDatagrams, other.InboundDatagrams), Add(EndpointReceiveTicks, other.EndpointReceiveTicks),
            Add(SecureReceiveTicks, other.SecureReceiveTicks), Add(FrameAdmissionTicks, other.FrameAdmissionTicks),
            Add(ReliableCompactionTicks, other.ReliableCompactionTicks),
            Add(ReceivedCompactionTicks, other.ReceivedCompactionTicks),
            Add(OutboundUnreliableFrames, other.OutboundUnreliableFrames),
            Add(InboundUnreliableFrames, other.InboundUnreliableFrames),
            Add(LaneSelectionPasses, other.LaneSelectionPasses),
            Add(ReliableSlotProbes, other.ReliableSlotProbes),
            Add(IndependentSlotProbes, other.IndependentSlotProbes),
            Add(UnreliableSlotProbes, other.UnreliableSlotProbes),
            Add(ExcludedSelectionProbes, other.ExcludedSelectionProbes),
            Add(EmptySelectionAttempts, other.EmptySelectionAttempts),
            Add(ReadySelections, other.ReadySelections));
    }

    private static ulong Difference(ulong start, ulong end) => end >= start
        ? end - start
        : throw new InvalidOperationException("A cumulative protocol timing counter moved backwards.");
    private static ulong Add(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

/// <summary>
/// Cold lifetime counters for the opt-in same-peer unreliable Data aggregate.
/// Histogram fields count secure Data datagrams by embedded frame count; the final
/// bucket is eight or more so the snapshot remains allocation-free.
/// </summary>
public readonly record struct NetworkUnreliableAggregationSnapshot(
    int ConfiguredMaximumFramesPerDatagram,
    NetworkUnreliableAggregationScope ConfiguredScope,
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
    ulong SentPlaintextAtMostQuarterMtu,
    ulong SentPlaintextAtMostHalfMtu,
    ulong SentPlaintextAtMostThreeQuarterMtu,
    ulong SentPlaintextAtMostNineTenthsMtu,
    ulong SentPlaintextAboveNineTenthsMtu,
    ulong SentSameLaneDatagrams,
    ulong SentCrossLaneDatagrams,
    ulong SentInputOwnerStateDatagrams,
    ulong SentOwnerWorldStateDatagrams,
    ulong SentInputWorldStateDatagrams,
    ulong SentInputOwnerWorldStateDatagrams,
    ulong SentOtherMultiLaneDatagrams,
    int MaximumSentFramesPerDatagram,
    int MaximumReceivedFramesPerDatagram,
    int MaximumSentPlaintextBytes,
    int MaximumReceivedPlaintextBytes)
{
    public static NetworkUnreliableAggregationSnapshot Delta(
        in NetworkUnreliableAggregationSnapshot start,
        in NetworkUnreliableAggregationSnapshot end)
    {
        if (start.ConfiguredMaximumFramesPerDatagram != end.ConfiguredMaximumFramesPerDatagram ||
            start.ConfiguredScope != end.ConfiguredScope)
            throw new ArgumentException("Unreliable aggregation snapshots use different policies.");
        return new(end.ConfiguredMaximumFramesPerDatagram, end.ConfiguredScope,
            Difference(start.SentFrames, end.SentFrames),
            Difference(start.SentDatagrams, end.SentDatagrams),
            Difference(start.ReceivedFrames, end.ReceivedFrames),
            Difference(start.ReceivedDatagrams, end.ReceivedDatagrams),
            Difference(start.SentDatagramsWithTwoOrMoreFrames, end.SentDatagramsWithTwoOrMoreFrames),
            Difference(start.ReceivedDatagramsWithTwoOrMoreFrames, end.ReceivedDatagramsWithTwoOrMoreFrames),
            Difference(start.SentHistogramOne, end.SentHistogramOne),
            Difference(start.SentHistogramTwo, end.SentHistogramTwo),
            Difference(start.SentHistogramThree, end.SentHistogramThree),
            Difference(start.SentHistogramFour, end.SentHistogramFour),
            Difference(start.SentHistogramFive, end.SentHistogramFive),
            Difference(start.SentHistogramSix, end.SentHistogramSix),
            Difference(start.SentHistogramSeven, end.SentHistogramSeven),
            Difference(start.SentHistogramEightOrMore, end.SentHistogramEightOrMore),
            Difference(start.ReceivedHistogramOne, end.ReceivedHistogramOne),
            Difference(start.ReceivedHistogramTwo, end.ReceivedHistogramTwo),
            Difference(start.ReceivedHistogramThree, end.ReceivedHistogramThree),
            Difference(start.ReceivedHistogramFour, end.ReceivedHistogramFour),
            Difference(start.ReceivedHistogramFive, end.ReceivedHistogramFive),
            Difference(start.ReceivedHistogramSix, end.ReceivedHistogramSix),
            Difference(start.ReceivedHistogramSeven, end.ReceivedHistogramSeven),
            Difference(start.ReceivedHistogramEightOrMore, end.ReceivedHistogramEightOrMore),
            Difference(start.SentPlaintextBytes, end.SentPlaintextBytes),
            Difference(start.ReceivedPlaintextBytes, end.ReceivedPlaintextBytes),
            Difference(start.SentPlaintextAtMostQuarterMtu, end.SentPlaintextAtMostQuarterMtu),
            Difference(start.SentPlaintextAtMostHalfMtu, end.SentPlaintextAtMostHalfMtu),
            Difference(start.SentPlaintextAtMostThreeQuarterMtu, end.SentPlaintextAtMostThreeQuarterMtu),
            Difference(start.SentPlaintextAtMostNineTenthsMtu, end.SentPlaintextAtMostNineTenthsMtu),
            Difference(start.SentPlaintextAboveNineTenthsMtu, end.SentPlaintextAboveNineTenthsMtu),
            Difference(start.SentSameLaneDatagrams, end.SentSameLaneDatagrams),
            Difference(start.SentCrossLaneDatagrams, end.SentCrossLaneDatagrams),
            Difference(start.SentInputOwnerStateDatagrams, end.SentInputOwnerStateDatagrams),
            Difference(start.SentOwnerWorldStateDatagrams, end.SentOwnerWorldStateDatagrams),
            Difference(start.SentInputWorldStateDatagrams, end.SentInputWorldStateDatagrams),
            Difference(start.SentInputOwnerWorldStateDatagrams, end.SentInputOwnerWorldStateDatagrams),
            Difference(start.SentOtherMultiLaneDatagrams, end.SentOtherMultiLaneDatagrams),
            end.MaximumSentFramesPerDatagram, end.MaximumReceivedFramesPerDatagram,
            end.MaximumSentPlaintextBytes, end.MaximumReceivedPlaintextBytes);
    }

    public NetworkUnreliableAggregationSnapshot Add(
        in NetworkUnreliableAggregationSnapshot other)
    {
        if (ConfiguredMaximumFramesPerDatagram != other.ConfiguredMaximumFramesPerDatagram ||
            ConfiguredScope != other.ConfiguredScope)
            throw new ArgumentException("Unreliable aggregation snapshots use different policies.");
        return new(ConfiguredMaximumFramesPerDatagram, ConfiguredScope,
            Add(SentFrames, other.SentFrames), Add(SentDatagrams, other.SentDatagrams),
            Add(ReceivedFrames, other.ReceivedFrames), Add(ReceivedDatagrams, other.ReceivedDatagrams),
            Add(SentDatagramsWithTwoOrMoreFrames, other.SentDatagramsWithTwoOrMoreFrames),
            Add(ReceivedDatagramsWithTwoOrMoreFrames, other.ReceivedDatagramsWithTwoOrMoreFrames),
            Add(SentHistogramOne, other.SentHistogramOne), Add(SentHistogramTwo, other.SentHistogramTwo),
            Add(SentHistogramThree, other.SentHistogramThree), Add(SentHistogramFour, other.SentHistogramFour),
            Add(SentHistogramFive, other.SentHistogramFive), Add(SentHistogramSix, other.SentHistogramSix),
            Add(SentHistogramSeven, other.SentHistogramSeven), Add(SentHistogramEightOrMore, other.SentHistogramEightOrMore),
            Add(ReceivedHistogramOne, other.ReceivedHistogramOne), Add(ReceivedHistogramTwo, other.ReceivedHistogramTwo),
            Add(ReceivedHistogramThree, other.ReceivedHistogramThree), Add(ReceivedHistogramFour, other.ReceivedHistogramFour),
            Add(ReceivedHistogramFive, other.ReceivedHistogramFive), Add(ReceivedHistogramSix, other.ReceivedHistogramSix),
            Add(ReceivedHistogramSeven, other.ReceivedHistogramSeven), Add(ReceivedHistogramEightOrMore, other.ReceivedHistogramEightOrMore),
            Add(SentPlaintextBytes, other.SentPlaintextBytes), Add(ReceivedPlaintextBytes, other.ReceivedPlaintextBytes),
            Add(SentPlaintextAtMostQuarterMtu, other.SentPlaintextAtMostQuarterMtu),
            Add(SentPlaintextAtMostHalfMtu, other.SentPlaintextAtMostHalfMtu),
            Add(SentPlaintextAtMostThreeQuarterMtu, other.SentPlaintextAtMostThreeQuarterMtu),
            Add(SentPlaintextAtMostNineTenthsMtu, other.SentPlaintextAtMostNineTenthsMtu),
            Add(SentPlaintextAboveNineTenthsMtu, other.SentPlaintextAboveNineTenthsMtu),
            Add(SentSameLaneDatagrams, other.SentSameLaneDatagrams), Add(SentCrossLaneDatagrams, other.SentCrossLaneDatagrams),
            Add(SentInputOwnerStateDatagrams, other.SentInputOwnerStateDatagrams),
            Add(SentOwnerWorldStateDatagrams, other.SentOwnerWorldStateDatagrams),
            Add(SentInputWorldStateDatagrams, other.SentInputWorldStateDatagrams),
            Add(SentInputOwnerWorldStateDatagrams, other.SentInputOwnerWorldStateDatagrams),
            Add(SentOtherMultiLaneDatagrams, other.SentOtherMultiLaneDatagrams),
            Math.Max(MaximumSentFramesPerDatagram, other.MaximumSentFramesPerDatagram),
            Math.Max(MaximumReceivedFramesPerDatagram, other.MaximumReceivedFramesPerDatagram),
            Math.Max(MaximumSentPlaintextBytes, other.MaximumSentPlaintextBytes),
            Math.Max(MaximumReceivedPlaintextBytes, other.MaximumReceivedPlaintextBytes));
    }

    private static ulong Difference(ulong start, ulong end) => end >= start
        ? end - start
        : throw new InvalidOperationException("A cumulative unreliable aggregation counter moved backwards.");
    private static ulong Add(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

/// <summary>
/// Detailed-only cumulative census of unreliable frames which were already ready
/// at an immediate emit opportunity. Cap estimates never delay or mutate traffic.
/// </summary>
public readonly record struct NetworkUnreliableOpportunitySnapshot(
    ulong Inspections,
    ulong Empty,
    ulong OneFrame,
    ulong TwoFrames,
    ulong ThreeFrames,
    ulong FourFrames,
    ulong FiveToEightFrames,
    ulong NineOrMoreFrames,
    ulong ReadyFrames,
    ulong PlaintextBytes,
    ulong ScalarDatagrams,
    ulong CrossLaneCapTwoDatagrams,
    ulong CrossLaneCapFourDatagrams,
    ulong CrossLaneCapEightDatagrams,
    ulong CrossLaneUnlimitedDatagrams,
    ulong SameLaneCapTwoDatagrams,
    ulong SameLaneCapFourDatagrams,
    ulong SameLaneCapEightDatagrams,
    ulong SameLaneUnlimitedDatagrams,
    ulong SingleLaneInspections,
    ulong MultiLaneInspections,
    ulong ReadyInputFrames,
    ulong ReadyOwnerStateFrames,
    ulong ReadyWorldStateFrames,
    ulong ReadyDisposableFrames,
    ulong ReadyOtherFrames,
    int MaximumReadyFrames)
{
    public static NetworkUnreliableOpportunitySnapshot Delta(
        in NetworkUnreliableOpportunitySnapshot start,
        in NetworkUnreliableOpportunitySnapshot end) => new(
        D(start.Inspections, end.Inspections), D(start.Empty, end.Empty),
        D(start.OneFrame, end.OneFrame), D(start.TwoFrames, end.TwoFrames),
        D(start.ThreeFrames, end.ThreeFrames), D(start.FourFrames, end.FourFrames),
        D(start.FiveToEightFrames, end.FiveToEightFrames),
        D(start.NineOrMoreFrames, end.NineOrMoreFrames),
        D(start.ReadyFrames, end.ReadyFrames), D(start.PlaintextBytes, end.PlaintextBytes),
        D(start.ScalarDatagrams, end.ScalarDatagrams),
        D(start.CrossLaneCapTwoDatagrams, end.CrossLaneCapTwoDatagrams),
        D(start.CrossLaneCapFourDatagrams, end.CrossLaneCapFourDatagrams),
        D(start.CrossLaneCapEightDatagrams, end.CrossLaneCapEightDatagrams),
        D(start.CrossLaneUnlimitedDatagrams, end.CrossLaneUnlimitedDatagrams),
        D(start.SameLaneCapTwoDatagrams, end.SameLaneCapTwoDatagrams),
        D(start.SameLaneCapFourDatagrams, end.SameLaneCapFourDatagrams),
        D(start.SameLaneCapEightDatagrams, end.SameLaneCapEightDatagrams),
        D(start.SameLaneUnlimitedDatagrams, end.SameLaneUnlimitedDatagrams),
        D(start.SingleLaneInspections, end.SingleLaneInspections),
        D(start.MultiLaneInspections, end.MultiLaneInspections),
        D(start.ReadyInputFrames, end.ReadyInputFrames),
        D(start.ReadyOwnerStateFrames, end.ReadyOwnerStateFrames),
        D(start.ReadyWorldStateFrames, end.ReadyWorldStateFrames),
        D(start.ReadyDisposableFrames, end.ReadyDisposableFrames),
        D(start.ReadyOtherFrames, end.ReadyOtherFrames),
        end.MaximumReadyFrames);

    public NetworkUnreliableOpportunitySnapshot Add(
        in NetworkUnreliableOpportunitySnapshot other) => new(
        A(Inspections, other.Inspections), A(Empty, other.Empty),
        A(OneFrame, other.OneFrame), A(TwoFrames, other.TwoFrames),
        A(ThreeFrames, other.ThreeFrames), A(FourFrames, other.FourFrames),
        A(FiveToEightFrames, other.FiveToEightFrames), A(NineOrMoreFrames, other.NineOrMoreFrames),
        A(ReadyFrames, other.ReadyFrames), A(PlaintextBytes, other.PlaintextBytes),
        A(ScalarDatagrams, other.ScalarDatagrams),
        A(CrossLaneCapTwoDatagrams, other.CrossLaneCapTwoDatagrams),
        A(CrossLaneCapFourDatagrams, other.CrossLaneCapFourDatagrams),
        A(CrossLaneCapEightDatagrams, other.CrossLaneCapEightDatagrams),
        A(CrossLaneUnlimitedDatagrams, other.CrossLaneUnlimitedDatagrams),
        A(SameLaneCapTwoDatagrams, other.SameLaneCapTwoDatagrams),
        A(SameLaneCapFourDatagrams, other.SameLaneCapFourDatagrams),
        A(SameLaneCapEightDatagrams, other.SameLaneCapEightDatagrams),
        A(SameLaneUnlimitedDatagrams, other.SameLaneUnlimitedDatagrams),
        A(SingleLaneInspections, other.SingleLaneInspections),
        A(MultiLaneInspections, other.MultiLaneInspections),
        A(ReadyInputFrames, other.ReadyInputFrames),
        A(ReadyOwnerStateFrames, other.ReadyOwnerStateFrames),
        A(ReadyWorldStateFrames, other.ReadyWorldStateFrames),
        A(ReadyDisposableFrames, other.ReadyDisposableFrames),
        A(ReadyOtherFrames, other.ReadyOtherFrames),
        Math.Max(MaximumReadyFrames, other.MaximumReadyFrames));

    private static ulong D(ulong start, ulong end) => end >= start
        ? end - start
        : throw new InvalidOperationException("A cumulative unreliable opportunity counter moved backwards.");
    private static ulong A(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

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
    ulong EncryptionInvocations,
    ulong EncryptionPlaintextBytesAttempted,
    ulong EncryptedPlaintextBytes,
    ulong DecryptedPlaintextBytes,
    ulong ReliableFragmentsSent,
    ulong ReliableRetransmittedWireBytes,
    int ReliableQueued,
    int ReliableQueuedHighWater,
    int IndependentUnreliableQueued,
    int IndependentUnreliableQueuedHighWater,
    int ReceivedQueued,
    int ReceivedQueuedHighWater,
    bool AcknowledgementPending,
    int CongestionWindowPackets,
    int InFlightPackets,
    int InFlightPacketsHighWater,
    ulong SmoothedRoundTripSteps,
    ulong RetransmissionTimeoutSteps,
    long ReservedBytes,
    NetworkRetainedPayloadSnapshot RetainedPayloads,
    NetworkUnreliableAggregationSnapshot UnreliableAggregation,
    NetworkUnreliableOpportunitySnapshot UnreliableOpportunity);

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
