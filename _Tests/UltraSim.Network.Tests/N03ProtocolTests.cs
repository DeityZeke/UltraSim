using System.Security.Cryptography;
using UltraSim.Network;

internal static class N03ProtocolTests
{
    private static readonly NetworkProtocolLimits s_limits = new(
        16, 16, 4_096, 32, 256, 16, 16, 2, 64, 4, 32, 16);
    private static readonly DeterministicDatagramLimits s_datagrams = new(256, 512);
    private static readonly NetworkProtocolManifest s_manifest = CreateManifest();

    internal static void RunAll()
    {
        Run("HandshakeUsesCookiePinnedIdentityAndProtocolAgreement", HandshakeUsesCookiePinnedIdentityAndProtocolAgreement);
        Run("SecureOuterFramingIsCanonicalAndExact", SecureOuterFramingIsCanonicalAndExact);
        Run("ReliableLanesSurviveLossDuplicationCorruptionAndReorder", ReliableLanesSurviveImpairment);
        Run("ReliableDeliveryRecoversAfterWholeWindowLoss", ReliableDeliveryRecoversAfterWholeWindowLoss);
        Run("ReliableDeliveryRecoversAfterWholeWindowAckLoss", ReliableDeliveryRecoversAfterWholeWindowAckLoss);
        Run("UnreliableLanesApplySequencingAndCoalescing", UnreliableLanesApplySequencingAndCoalescing);
        Run("UnreliableDataDoesNotElicitTransportAcknowledgements", UnreliableDataDoesNotElicitTransportAcknowledgements);
        Run("MixedTrafficAcknowledgesReliableDataOnly", MixedTrafficAcknowledgesReliableDataOnly);
        Run("UnreliableBatchCensusIsExactAndNonMutating", UnreliableBatchCensusIsExactAndNonMutating);
        Run("ProtocolStageTimingIsOptInAndResettable", ProtocolStageTimingIsOptInAndResettable);
        Run("IndependentWorldStateContainersDoNotCoalesce", IndependentWorldStateContainersDoNotCoalesce);
        Run("UnreliableMessageFamiliesOwnIndependentStreams", UnreliableMessageFamiliesOwnIndependentStreams);
        Run("SequencedCapacityDropAdvancesStaleFloor", SequencedCapacityDropAdvancesStaleFloor);
        Run("BulkFragmentationReassemblesExactlyAtSmallMtu", BulkFragmentationReassemblesExactly);
        Run("ReplayAndAuthenticationRejectBeforePublication", ReplayAndAuthenticationRejectBeforePublication);
        Run("AuthenticatedReplayIsIndependentFromDeliveryBackpressure", AuthenticatedReplayIsIndependentFromDeliveryBackpressure);
        Run("AuthenticatedRegionsRejectWithoutStatePublication", AuthenticatedRegionsRejectWithoutStatePublication);
        Run("ReceiveBackpressureRetainsReliableMessage", ReceiveBackpressureRetainsReliableMessage);
        Run("SecurityEpochRotationIsAuthenticatedAndFailClosed", SecurityEpochRotationIsAuthenticatedAndFailClosed);
        Run("HostileNextEpochProbesReuseOnePreparedCipher", HostileNextEpochProbesReuseOnePreparedCipher);
        Run("ProtocolCapacitiesRejectTransactionally", ProtocolCapacitiesRejectTransactionally);
        Run("PayloadReservationsFollowManifestAndLaneContracts", PayloadReservationsFollowManifestAndLaneContracts);
        Run("RetainedPayloadPressureIsExact", RetainedPayloadPressureIsExact);
        Run("RetainedPayloadByteBudgetsRejectAndCompactTransactionally", RetainedPayloadByteBudgetsRejectAndCompactTransactionally);
        Run("InboundBudgetCannotStarveOutboundControl", InboundBudgetCannotStarveOutboundControl);
        Run("AbortiveShutdownIsBalancedAndIdempotent", AbortiveShutdownIsBalancedAndIdempotent);
        Run("N03WarmedSecureDeliveryAllocatesZero", WarmedSecureDeliveryAllocatesZero);
    }

    private static void SecureOuterFramingIsCanonicalAndExact()
    {
        var header = new NetworkSecureDatagramHeader(
            3, new ConnectionId(7, 2), new SecurityEpoch(4), new PacketNumber(9),
            NetworkSecureDatagramKind.Data, 3);
        Span<byte> datagram = stackalloc byte[
            NetworkSecureDatagramFraming.HeaderBytes + 3 + NetworkSecureDatagramFraming.TagBytes];
        var writer = new NetworkPacketWriter(datagram[..NetworkSecureDatagramFraming.HeaderBytes]);
        True(NetworkSecureDatagramFraming.TryWriteHeader(ref writer, header));
        datagram[NetworkSecureDatagramFraming.HeaderBytes] = 1;
        True(NetworkSecureDatagramFraming.TryReadExact(datagram, out NetworkSecureDatagramHeader decoded,
            out ReadOnlySpan<byte> associated, out ReadOnlySpan<byte> ciphertext, out ReadOnlySpan<byte> tag));
        Equal(header, decoded);
        Equal(NetworkSecureDatagramFraming.HeaderBytes, associated.Length);
        Equal(3, ciphertext.Length);
        Equal(NetworkSecureDatagramFraming.TagBytes, tag.Length);
        for (int length = 0; length < datagram.Length; length++)
            False(NetworkSecureDatagramFraming.TryReadExact(datagram[..length], out _, out _, out _, out _));
        Span<byte> trailing = stackalloc byte[datagram.Length + 1];
        datagram.CopyTo(trailing);
        False(NetworkSecureDatagramFraming.TryReadExact(trailing, out _, out _, out _, out _));
        datagram[25] = 1;
        False(NetworkSecureDatagramFraming.TryReadExact(datagram, out _, out _, out _, out _));
    }

    private static void HandshakeUsesCookiePinnedIdentityAndProtocolAgreement()
    {
        using var server = NetworkServerIdentity.CreateEphemeral();
        True(ProtocolLoopback.TryCreate(s_limits, s_datagrams,
            new NetworkImpairmentOptions(0xC001, 200, 100, 75, 3),
            out ProtocolLoopback? loopback, out ProtocolHandshakeSnapshot handshake,
            s_manifest, server, server.PublicKey.ToArray()));
        using (loopback!)
        {
            True(handshake.Established);
            True(handshake.CookieChallenges > 0);
            True(handshake.CookieValidations > 0);
            Equal(1UL, handshake.KeyExchanges);
            True(handshake.Connection.IsValid);
            Equal(new SecurityEpoch(1), handshake.SecurityEpoch);
        }

        using var untrusted = NetworkServerIdentity.CreateEphemeral();
        False(ProtocolLoopback.TryCreate(s_limits, s_datagrams, default,
            out ProtocolLoopback? rejected, out _, s_manifest, server,
            untrusted.PublicKey.ToArray(), maximumHandshakeSteps: 64));
        True(rejected is null);

        NetworkMessageDescriptor[] descriptors = s_manifest.Messages.ToArray();
        NetworkProtocolManifest incompatible = NetworkProtocolManifest.Create(4, descriptors);
        False(incompatible.Fingerprint == s_manifest.Fingerprint);
        False(ProtocolLoopback.TryCreate(s_limits, s_datagrams, default,
            out ProtocolLoopback? mismatch, out _, s_manifest,
            maximumHandshakeSteps: 64, serverManifest: incompatible));
        True(mismatch is null);
    }

    private static void ReliableLanesSurviveImpairment()
    {
        using ProtocolLoopback loopback = Create(new(0xBEEF, 250, 200, 100, 5));
        byte[] control = { 1, 2, 3, 4 };
        byte[] events = { 5, 6, 7 };
        byte[] bulk = Enumerable.Range(0, 1_500).Select(static value => unchecked((byte)value)).ToArray();
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), control));
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), events));
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Bulk), bulk));

        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 3, 20_000);
        AssertReceive(loopback.Server, NetworkLane.Control, control);
        AssertReceive(loopback.Server, NetworkLane.Event, events);
        AssertReceive(loopback.Server, NetworkLane.Bulk, bulk);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 20_000);
        NetworkProtocolSnapshot client = loopback.Client.GetSnapshot();
        True(client.ReliableRetransmissions > 0);
        True(client.ReliableRetransmittedWireBytes > 0);
        True(client.ReliableFragmentsSent > client.ReliableAcknowledgements);
        True(client.DataPacketsSent > 0);
        True(client.EncryptedPlaintextBytes > 0);
        True(client.ReliableAcknowledgements >= 3);
    }

    private static void ReliableDeliveryRecoversAfterWholeWindowLoss()
    {
        var datagrams = new DeterministicDatagramLimits(64, 256);
        NetworkProtocolLimits limits = s_limits with
        {
            InitialCongestionWindowPackets = 4,
            MaximumCongestionWindowPackets = 4,
            MinimumRetransmissionSteps = 2,
        };
        using RawPeerPair pair = new(s_manifest, limits, datagrams);
        byte[] payload = Enumerable.Range(0, 700).Select(static value => unchecked((byte)value)).ToArray();
        Equal(NetworkQueueStatus.Accepted, pair.Sender.TryQueue(Id(NetworkLane.Bulk), payload));
        pair.Sender.Emit(0);
        Equal(4, pair.Sender.GetSnapshot().InFlightPackets);
        Equal(4, Drain(pair.Link.B)); // Lose the complete initial congestion window.

        // Keep losing complete retry windows for several RTOs. Timeout discovery must
        // continue even while the prior flight filled the congestion window.
        for (ulong step = 1; step <= 8; step++)
        {
            pair.Sender.Emit(step);
            _ = Drain(pair.Link.B);
        }
        True(pair.Sender.GetSnapshot().ReliableRetransmissions >= 4);

        Drive(pair, 9, () => pair.Receiver.ReceivedQueued == 1, 128);
        AssertReceive(pair.Receiver, NetworkLane.Bulk, payload);
        Drive(pair, 137, () => pair.Sender.ReliableQueued == 0, 128);
    }

    private static void ReliableDeliveryRecoversAfterWholeWindowAckLoss()
    {
        var datagrams = new DeterministicDatagramLimits(64, 256);
        NetworkProtocolLimits limits = s_limits with
        {
            InitialCongestionWindowPackets = 4,
            MaximumCongestionWindowPackets = 4,
            MinimumRetransmissionSteps = 2,
        };
        using RawPeerPair pair = new(s_manifest, limits, datagrams);
        byte[] payload = Enumerable.Range(0, 700).Select(static value => unchecked((byte)(value * 17))).ToArray();
        Equal(NetworkQueueStatus.Accepted, pair.Sender.TryQueue(Id(NetworkLane.Bulk), payload));
        pair.Sender.Emit(0);
        pair.Receiver.ProcessInbound(0);
        Equal(1, pair.Receiver.ReceivedQueued);
        pair.Receiver.Emit(0);
        True(Drain(pair.Link.A) > 0); // Lose the ACK covering the entire flight.
        Equal(4, pair.Sender.GetSnapshot().InFlightPackets);

        Drive(pair, 1, () => pair.Sender.ReliableQueued == 0, 128);
        Equal(1, pair.Receiver.ReceivedQueued);
        AssertReceive(pair.Receiver, NetworkLane.Bulk, payload);
        True(pair.Sender.GetSnapshot().ReliableRetransmissions > 0);
        True(pair.Receiver.GetSnapshot().AcknowledgementPacketsSent >= 2);

        NetworkProtocolLimits onePacket = limits with
        {
            InitialCongestionWindowPackets = 1,
            MaximumCongestionWindowPackets = 1,
        };
        using RawPeerPair single = new(s_manifest, onePacket, datagrams);
        Equal(NetworkQueueStatus.Accepted,
            single.Sender.TryQueue(Id(NetworkLane.Control), stackalloc byte[] { 1 }));
        single.Sender.Emit(0);
        Equal(1, Drain(single.Link.B));
        single.Sender.Emit(2);
        Equal(1, single.Sender.GetSnapshot().CongestionWindowPackets);
    }

    private static void UnreliableLanesApplySequencingAndCoalescing()
    {
        using ProtocolLoopback loopback = Create(new(77, 0, 500, 0, 7));
        Span<byte> payload = stackalloc byte[8];
        for (ulong value = 0; value < 10; value++)
        {
            WriteUInt64(payload, value);
            Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Input), payload));
            Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.OwnerState), payload));
        }
        byte[] disposable = { 99 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Disposable), disposable));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued >= 3, 1_000);

        var seen = new Dictionary<NetworkLane, ulong>();
        while (loopback.Server.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> delivered))
        {
            seen[message.Lane] = message.Lane == NetworkLane.Disposable ? delivered[0] : ReadUInt64(delivered);
            True(loopback.Server.TryConsumeReceived());
        }
        Equal(9UL, seen[NetworkLane.Input]);
        Equal(9UL, seen[NetworkLane.OwnerState]);
        Equal(99UL, seen[NetworkLane.Disposable]);
    }

    private static void UnreliableDataDoesNotElicitTransportAcknowledgements()
    {
        using ProtocolLoopback loopback = Create();
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.Input), stackalloc byte[] { 1 }));
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.OwnerState), stackalloc byte[] { 2 }));

        Drive(loopback, () => loopback.Server.ReceivedQueued == 2, 100);
        AssertReceive(loopback.Server, NetworkLane.Input, stackalloc byte[] { 1 });
        AssertReceive(loopback.Server, NetworkLane.OwnerState, stackalloc byte[] { 2 });
        for (int step = 0; step < 16; step++) loopback.Pump();

        NetworkProtocolSnapshot sender = loopback.Client.GetSnapshot();
        NetworkProtocolSnapshot receiver = loopback.Server.GetSnapshot();
        Equal(2UL, sender.DataPacketsSent);
        Equal(0UL, receiver.AcknowledgementPacketsSent);
        Equal(0UL, sender.ReliableAcknowledgements);
        Equal(0UL, sender.SmoothedRoundTripSteps);
        Equal(2UL, receiver.ReceivedPackets);
    }

    private static void MixedTrafficAcknowledgesReliableDataOnly()
    {
        using ProtocolLoopback loopback = Create();
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.Control), stackalloc byte[] { 7 }));
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.Input), stackalloc byte[] { 8 }));

        Drive(loopback, () => loopback.Server.ReceivedQueued == 2, 100);
        AssertReceive(loopback.Server, NetworkLane.Control, stackalloc byte[] { 7 });
        AssertReceive(loopback.Server, NetworkLane.Input, stackalloc byte[] { 8 });
        Drive(loopback, () => loopback.Client.ReliableQueued == 0, 100);

        NetworkProtocolSnapshot sender = loopback.Client.GetSnapshot();
        NetworkProtocolSnapshot receiver = loopback.Server.GetSnapshot();
        Equal(2UL, sender.DataPacketsSent);
        Equal(1UL, sender.ReliableAcknowledgements);
        True(receiver.AcknowledgementPacketsSent > 0);
        True(receiver.AcknowledgementPacketsSent < sender.DataPacketsSent);
        True(sender.SmoothedRoundTripSteps > 0);
    }

    private static void UnreliableBatchCensusIsExactAndNonMutating()
    {
        using ProtocolLoopback loopback = Create();
        byte[] payload = new byte[200];
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Input), payload));
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.OwnerState), payload));
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.WorldState), payload));

        NetworkUnreliableBatchOpportunity opportunity =
            loopback.Client.InspectUnreliableBatchOpportunity();
        Equal(3, opportunity.ReadyFrames);
        Equal(3 * (NetworkProtocolDataHeader.EncodedBytes + payload.Length),
            opportunity.PlaintextBytes);
        Equal(2, opportunity.GreedyDatagrams);
        Equal(1, loopback.Client.IndependentUnreliableQueued);

        Drive(loopback, () => loopback.Server.ReceivedQueued == 3, 100);
        Equal(0, loopback.Client.InspectUnreliableBatchOpportunity().ReadyFrames);
    }

    private static void ProtocolStageTimingIsOptInAndResettable()
    {
        using ProtocolLoopback loopback = Create();
        Equal(default, loopback.Client.GetStageTiming());
        Equal(default, loopback.Server.GetStageTiming());
        loopback.Client.ResetStageTiming(enabled: true);
        loopback.Server.ResetStageTiming(enabled: true);

        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.Control), stackalloc byte[] { 4, 2 }));
        Drive(loopback, () => loopback.Server.ReceivedQueued == 1, 100);
        AssertReceive(loopback.Server, NetworkLane.Control, stackalloc byte[] { 4, 2 });
        Drive(loopback, () => loopback.Client.ReliableQueued == 0, 100);

        NetworkProtocolStageTiming sender = loopback.Client.GetStageTiming();
        NetworkProtocolStageTiming receiver = loopback.Server.GetStageTiming();
        True(sender.EmitTicks > 0);
        True(sender.SelectionTicks > 0);
        True(sender.FrameEncodeTicks > 0);
        True(sender.SecureSendTicks > 0);
        True(sender.EndpointSendTicks > 0);
        True(receiver.InboundTicks > 0);
        True(receiver.EndpointReceiveTicks > 0);
        True(receiver.SecureReceiveTicks > 0);
        True(receiver.FrameAdmissionTicks > 0);

        loopback.Client.ResetStageTiming(enabled: false);
        loopback.Server.ResetStageTiming(enabled: false);
        Equal(default, loopback.Client.GetStageTiming());
        Equal(default, loopback.Server.GetStageTiming());
    }

    private static void IndependentWorldStateContainersDoNotCoalesce()
    {
        using ProtocolLoopback loopback = Create(default);
        Span<byte> payload = stackalloc byte[8];
        for (ulong value = 0; value < 10; value++)
        {
            WriteUInt64(payload, value);
            Equal(NetworkQueueStatus.Accepted,
                loopback.Client.TryQueue(Id(NetworkLane.WorldState), payload));
        }
        Equal(10, loopback.Client.IndependentUnreliableQueued);
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 10, 1_000);

        Span<bool> seen = stackalloc bool[10];
        while (loopback.Server.TryPeekReceived(out NetworkDeliveredMessage message,
                   out ReadOnlySpan<byte> delivered))
        {
            Equal(NetworkLane.WorldState, message.Lane);
            ulong value = ReadUInt64(delivered);
            True(value < 10);
            False(seen[(int)value]);
            seen[(int)value] = true;
            True(loopback.Server.TryConsumeReceived());
        }
        for (int index = 0; index < seen.Length; index++) True(seen[index]);
        Equal(0, loopback.Client.IndependentUnreliableQueued);

        NetworkProtocolLimits bounded = s_limits with { MaximumIndependentUnreliableMessages = 2 };
        using ProtocolLoopback capacity = Create(default, limits: bounded);
        Equal(NetworkQueueStatus.Accepted,
            capacity.Client.TryQueue(Id(NetworkLane.WorldState), stackalloc byte[] { 1 }));
        Equal(NetworkQueueStatus.Accepted,
            capacity.Client.TryQueue(Id(NetworkLane.WorldState), stackalloc byte[] { 2 }));
        Equal(NetworkQueueStatus.CapacityExceeded,
            capacity.Client.TryQueue(Id(NetworkLane.WorldState), stackalloc byte[] { 3 }));
    }

    private static void UnreliableMessageFamiliesOwnIndependentStreams()
    {
        NetworkMessageDescriptor[] descriptors = s_manifest.Messages.ToArray();
        Array.Resize(ref descriptors, descriptors.Length + 1);
        var secondId = new NetworkMessageId(100);
        descriptors[^1] = new(secondId, "n0.3.test.OwnerVitals", 1,
            NetworkLane.OwnerState, 4_096, 9_999);
        NetworkProtocolManifest manifest = NetworkProtocolManifest.Create(3, descriptors);
        True(ProtocolLoopback.TryCreate(s_limits, s_datagrams, default,
            out ProtocolLoopback? created, out _, manifest));
        using ProtocolLoopback loopback = created!;
        byte[] transform = { 1, 2 };
        byte[] vitals = { 9, 8, 7 };
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.OwnerState), transform));
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(secondId, vitals));
        Drive(loopback, () => loopback.Server.ReceivedQueued == 2, 1_000);

        var delivered = new Dictionary<NetworkMessageId, byte[]>();
        while (loopback.Server.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            Equal(0UL, message.LaneSequence.Value);
            delivered.Add(message.MessageId, payload.ToArray());
            True(loopback.Server.TryConsumeReceived());
        }
        True(delivered[Id(NetworkLane.OwnerState)].AsSpan().SequenceEqual(transform));
        True(delivered[secondId].AsSpan().SequenceEqual(vitals));
    }

    private static void SequencedCapacityDropAdvancesStaleFloor()
    {
        NetworkProtocolLimits limits = s_limits with { MaximumReceivedMessages = 1 };
        using RawPeerPair pair = new(s_manifest, limits, s_datagrams);
        NetworkMessageId message = Id(NetworkLane.OwnerState);

        pair.InjectData(message, NetworkLane.OwnerState, sequence: 8, packet: 0, stackalloc byte[] { 8 });
        Equal(1, pair.Receiver.ReceivedQueued);
        pair.InjectData(message, NetworkLane.OwnerState, sequence: 10, packet: 1, stackalloc byte[] { 10 });
        Equal(1UL, pair.Receiver.GetSnapshot().CapacityDrops);
        AssertReceive(pair.Receiver, NetworkLane.OwnerState, stackalloc byte[] { 8 });

        pair.InjectData(message, NetworkLane.OwnerState, sequence: 9, packet: 2, stackalloc byte[] { 9 });
        Equal(0, pair.Receiver.ReceivedQueued);
        Equal(1UL, pair.Receiver.GetSnapshot().SequencedDrops);
        pair.InjectData(message, NetworkLane.OwnerState, sequence: 11, packet: 3, stackalloc byte[] { 11 });
        AssertReceive(pair.Receiver, NetworkLane.OwnerState, stackalloc byte[] { 11 });
    }

    private static void BulkFragmentationReassemblesExactly()
    {
        var smallDatagrams = new DeterministicDatagramLimits(256, 256);
        using ProtocolLoopback loopback = Create(default, smallDatagrams);
        byte[] payload = Enumerable.Range(0, 4_000).Select(static value => unchecked((byte)(value * 31))).ToArray();
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Bulk), payload));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 5_000);
        AssertReceive(loopback.Server, NetworkLane.Bulk, payload);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 5_000);
        True(loopback.Client.GetSnapshot().SentPackets > 1);
    }

    private static void ReplayAndAuthenticationRejectBeforePublication()
    {
        using ProtocolLoopback loopback = Create(new(0xDEAD, 0, 400, 75, 3));
        byte[] payload = { 4, 8, 15, 16, 23, 42 };
        for (byte iteration = 0; iteration < 64; iteration++)
        {
            payload[0] = iteration;
            Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), payload));
            Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 10_000);
            AssertReceive(loopback.Server, NetworkLane.Event, payload);
            Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 10_000);
        }
        NetworkProtocolSnapshot server = loopback.Server.GetSnapshot();
        True(server.AuthenticationFailures > 0);
        True(server.ReplayRejections > 0);
        Equal(0, server.ReceivedQueued);
    }

    private static void AuthenticatedReplayIsIndependentFromDeliveryBackpressure()
    {
        NetworkProtocolLimits limits = s_limits with { MaximumReceivedMessages = 1 };
        using ProtocolLoopback loopback = Create(new(0xA11CE, 0, 1_000, 0, 0), s_datagrams, limits);
        byte[] first = { 1 };
        byte[] blocked = { 2 };

        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), first));
        Drive(loopback, () => loopback.Server.ReceivedQueued == 1, 1_000);
        Drive(loopback, () => loopback.Client.ReliableQueued == 0, 1_000);

        NetworkProtocolSnapshot before = loopback.Server.GetSnapshot();
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), blocked));
        loopback.Pump(); // Emit the packet and its forced duplicate.
        loopback.Pump(); // Authenticate one copy, defer delivery, and reject the duplicate as replay.

        NetworkProtocolSnapshot deferred = loopback.Server.GetSnapshot();
        Equal(before.CapacityDrops + 1, deferred.CapacityDrops);
        Equal(before.ReplayRejections + 1, deferred.ReplayRejections);
        Equal(before.AcknowledgementPacketsSent, deferred.AcknowledgementPacketsSent);
        Equal(1, loopback.Server.ReceivedQueued);
        Equal(1, loopback.Client.ReliableQueued);

        AssertReceive(loopback.Server, NetworkLane.Event, first);
        Drive(loopback, () => loopback.Server.ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Event, blocked);
        Drive(loopback, () => loopback.Client.ReliableQueued == 0, 1_000);
        True(loopback.Server.GetSnapshot().AcknowledgementPacketsSent >
            deferred.AcknowledgementPacketsSent);
    }

    private static void AuthenticatedRegionsRejectWithoutStatePublication()
    {
        var link = new DeterministicDatagramLink(s_datagrams);
        byte[] masterSecret = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var connection = new ConnectionId(7, 3);
        var epoch = new SecurityEpoch(1);
        using var sender = new NetworkProtocolPeer(
            link.A, s_manifest, s_limits, s_datagrams, connection, epoch, masterSecret,
            sendsClientToServer: true);
        using var receiver = new NetworkProtocolPeer(
            link.B, s_manifest, s_limits, s_datagrams, connection, epoch, masterSecret,
            sendsClientToServer: false);
        byte[] authenticatedInvalid = CreateAuthenticatedDatagram(
            masterSecret, connection, epoch, new PacketNumber(1), stackalloc byte[] { 0xFF });
        CryptographicOperations.ZeroMemory(masterSecret);

        byte[] expected = { 4, 8, 15, 16, 23, 42 };
        Equal(NetworkQueueStatus.Accepted, sender.TryQueue(Id(NetworkLane.Event), expected));
        sender.Emit(0);
        byte[] original = new byte[s_datagrams.MaximumDatagramBytes];
        True(link.B.TryReceive(original, out int originalLength));
        Array.Resize(ref original, originalLength);
        True(NetworkSecureDatagramFraming.TryReadExact(original, out NetworkSecureDatagramHeader header,
            out _, out ReadOnlySpan<byte> ciphertext, out _));
        True(ciphertext.Length > 0);

        byte[] headerTamper = (byte[])original.Clone();
        var headerWriter = new NetworkPacketWriter(
            headerTamper.AsSpan(0, NetworkSecureDatagramFraming.HeaderBytes));
        True(NetworkSecureDatagramFraming.TryWriteHeader(ref headerWriter,
            header with { PacketNumber = new PacketNumber(header.PacketNumber.Value + 1) }));
        True(NetworkSecureDatagramFraming.TryReadExact(headerTamper, out _, out _, out _, out _));

        byte[] ciphertextTamper = (byte[])original.Clone();
        ciphertextTamper[NetworkSecureDatagramFraming.HeaderBytes] ^= 0x40;
        byte[] tagTamper = (byte[])original.Clone();
        tagTamper[^1] ^= 0x80;

        AssertAuthenticationFailureWithoutPublication(link, receiver, headerTamper, 1);
        AssertAuthenticationFailureWithoutPublication(link, receiver, ciphertextTamper, 2);
        AssertAuthenticationFailureWithoutPublication(link, receiver, tagTamper, 3);

        NetworkProtocolSnapshot beforeInvalid = receiver.GetSnapshot();
        Equal(DatagramSendStatus.Accepted, link.A.TrySend(authenticatedInvalid));
        Equal(DatagramSendStatus.Accepted, link.A.TrySend(authenticatedInvalid));
        receiver.ProcessInbound(0);
        NetworkProtocolSnapshot afterInvalid = receiver.GetSnapshot();
        Equal(beforeInvalid.AuthenticationFailures, afterInvalid.AuthenticationFailures);
        Equal(beforeInvalid.ReplayRejections + 1, afterInvalid.ReplayRejections);
        Equal(beforeInvalid.ReceivedPackets, afterInvalid.ReceivedPackets);
        Equal(beforeInvalid.ReceivedQueued, afterInvalid.ReceivedQueued);
        Equal(beforeInvalid.ReceiveSecurityEpoch, afterInvalid.ReceiveSecurityEpoch);

        Equal(DatagramSendStatus.Accepted, link.A.TrySend(original));
        receiver.ProcessInbound(0);
        AssertReceive(receiver, NetworkLane.Event, expected);
        NetworkProtocolSnapshot accepted = receiver.GetSnapshot();
        Equal(1UL, accepted.ReceivedPackets);
        Equal(3UL, accepted.AuthenticationFailures);
        Equal(1UL, accepted.ReplayRejections);
        Equal(epoch, accepted.ReceiveSecurityEpoch);
    }

    private static byte[] CreateAuthenticatedDatagram(
        ReadOnlySpan<byte> masterSecret,
        ConnectionId connection,
        SecurityEpoch epoch,
        PacketNumber packet,
        ReadOnlySpan<byte> plaintext)
    {
        byte[] datagram = new byte[
            NetworkSecureDatagramFraming.HeaderBytes + plaintext.Length + NetworkSecureDatagramFraming.TagBytes];
        var header = new NetworkSecureDatagramHeader(
            s_manifest.Version, connection, epoch, packet, NetworkSecureDatagramKind.Data,
            checked((ushort)plaintext.Length));
        var writer = new NetworkPacketWriter(datagram.AsSpan(0, NetworkSecureDatagramFraming.HeaderBytes));
        True(NetworkSecureDatagramFraming.TryWriteHeader(ref writer, header));
        using var cryptography = new NetworkDirectionalCryptography(
            masterSecret, clientToServer: true, epoch);
        cryptography.Encrypt(packet, plaintext,
            datagram.AsSpan(NetworkSecureDatagramFraming.HeaderBytes, plaintext.Length),
            datagram.AsSpan(NetworkSecureDatagramFraming.HeaderBytes + plaintext.Length,
                NetworkSecureDatagramFraming.TagBytes),
            datagram.AsSpan(0, NetworkSecureDatagramFraming.HeaderBytes));
        return datagram;
    }

    private static void AssertAuthenticationFailureWithoutPublication(
        DeterministicDatagramLink link,
        NetworkProtocolPeer receiver,
        ReadOnlySpan<byte> datagram,
        ulong expectedFailures)
    {
        NetworkProtocolSnapshot before = receiver.GetSnapshot();
        Equal(DatagramSendStatus.Accepted, link.A.TrySend(datagram));
        receiver.ProcessInbound(0);
        NetworkProtocolSnapshot after = receiver.GetSnapshot();
        Equal(expectedFailures, after.AuthenticationFailures);
        Equal(before.ReplayRejections, after.ReplayRejections);
        Equal(before.ReceivedPackets, after.ReceivedPackets);
        Equal(before.ReceivedQueued, after.ReceivedQueued);
        Equal(before.CapacityDrops, after.CapacityDrops);
        Equal(before.SequencedDrops, after.SequencedDrops);
        Equal(before.ReceiveSecurityEpoch, after.ReceiveSecurityEpoch);
    }

    private static void ReceiveBackpressureRetainsReliableMessage()
    {
        NetworkProtocolLimits limits = s_limits with { MaximumReceivedMessages = 1 };
        using ProtocolLoopback loopback = Create(default, s_datagrams, limits);
        byte[] first = { 1 };
        byte[] second = { 2 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), first));
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Event), second));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 1_000);
        for (int step = 0; step < 64; step++) loopback.Pump();
        Equal(1, loopback.Server.GetSnapshot().ReceivedQueued);
        AssertReceive(loopback.Server, NetworkLane.Event, first);
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Event, second);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 1_000);
        True(loopback.Server.GetSnapshot().CapacityDrops > 0);
    }

    private static void SecurityEpochRotationIsAuthenticatedAndFailClosed()
    {
        using ProtocolLoopback loopback = Create();
        byte[] first = { 10 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), first));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Control, first);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 1_000);
        True(loopback.TryRotateSecurityEpoch());

        byte[] second = { 20 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), second));
        Equal(NetworkQueueStatus.Accepted, loopback.Server.TryQueue(Id(NetworkLane.Control), second));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1 &&
            loopback.Client.GetSnapshot().ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Control, second);
        AssertReceive(loopback.Client, NetworkLane.Control, second);
        Equal(new SecurityEpoch(2), loopback.Client.GetSnapshot().SendSecurityEpoch);
        Equal(new SecurityEpoch(2), loopback.Client.GetSnapshot().ReceiveSecurityEpoch);
        Equal(new SecurityEpoch(2), loopback.Server.GetSnapshot().SendSecurityEpoch);
        Equal(new SecurityEpoch(2), loopback.Server.GetSnapshot().ReceiveSecurityEpoch);
    }

    private static void HostileNextEpochProbesReuseOnePreparedCipher()
    {
        Span<byte> secret = stackalloc byte[32];
        RandomNumberGenerator.Fill(secret);
        using var cryptography = new NetworkDirectionalCryptography(
            secret, clientToServer: true, new SecurityEpoch(1));
        CryptographicOperations.ZeroMemory(secret);
        ulong prepared = cryptography.CipherDerivations;
        Span<byte> plaintext = stackalloc byte[1];
        Span<byte> ciphertext = stackalloc byte[1];
        Span<byte> tag = stackalloc byte[NetworkSecureDatagramFraming.TagBytes];
        Span<byte> associated = stackalloc byte[NetworkSecureDatagramFraming.HeaderBytes];
        for (ulong packet = 1; packet <= 1_000; packet++)
        {
            False(cryptography.TryDecryptAtEpoch(
                new SecurityEpoch(2), new PacketNumber(packet), ciphertext, tag,
                plaintext, associated, out bool advanced));
            False(advanced);
        }
        Equal(prepared, cryptography.CipherDerivations);
        Equal(new SecurityEpoch(1), cryptography.Epoch);

        Span<byte> exhaustedSecret = stackalloc byte[32];
        RandomNumberGenerator.Fill(exhaustedSecret);
        using var exhausted = new NetworkDirectionalCryptography(
            exhaustedSecret, clientToServer: true, new SecurityEpoch(uint.MaxValue));
        CryptographicOperations.ZeroMemory(exhaustedSecret);
        False(exhausted.TryAdvanceTo(new SecurityEpoch(uint.MaxValue)));
        Equal(1UL, exhausted.CipherDerivations);
    }

    private static void ProtocolCapacitiesRejectTransactionally()
    {
        NetworkProtocolLimits limits = s_limits with { MaximumReliableMessages = 1 };
        using ProtocolLoopback loopback = Create(default, s_datagrams, limits);
        byte[] payload = { 1, 2 };
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), payload));
        Equal(NetworkQueueStatus.CapacityExceeded, loopback.Client.TryQueue(Id(NetworkLane.Control), payload));
        Equal(1, loopback.Client.GetSnapshot().ReliableQueued);
        byte[] tooLarge = new byte[4_097];
        Equal(NetworkQueueStatus.MessageTooLarge, loopback.Client.TryQueue(Id(NetworkLane.Bulk), tooLarge));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 1_000);
        AssertReceive(loopback.Server, NetworkLane.Control, payload);
        Drive(loopback, () => loopback.Client.GetSnapshot().ReliableQueued == 0, 1_000);
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.Control), payload));
    }

    private static void PayloadReservationsFollowManifestAndLaneContracts()
    {
        using ProtocolLoopback loopback = Create();
        NetworkRetainedPayloadSnapshot payloads = loopback.Client.GetSnapshot().RetainedPayloads;
        int fragmentPayload = NetworkSecureDatagramFraming.MaximumPlaintextBytes(
            s_datagrams.MaximumDatagramBytes) - NetworkProtocolDataHeader.EncodedBytes;

        Equal(s_limits.MaximumReliableMessages * 4_096, payloads.ReliableCapacityBytes);
        Equal(s_limits.MaximumIndependentUnreliableMessages * fragmentPayload,
            payloads.IndependentCapacityBytes);
        Equal(3 * fragmentPayload, payloads.UnreliableStreamCapacityBytes);
        Equal(3 * 4_096, payloads.ReassemblyCapacityBytes);
        Equal(s_limits.MaximumReceivedMessages * 4_096, payloads.ReceivedCapacityBytes);
        long concreteEstimate = s_limits.EstimateReservedPayloadBytes(s_datagrams, s_manifest);
        long conservativeEstimate = s_limits.EstimateReservedPayloadBytes(s_datagrams, s_manifest.Count);
        True(concreteEstimate < conservativeEstimate);
        True(loopback.Client.GetSnapshot().ReservedBytes <
            conservativeEstimate);

        byte[] maximumIndependent = new byte[fragmentPayload];
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.WorldState), maximumIndependent));
        Equal(NetworkQueueStatus.MessageTooLarge,
            loopback.Client.TryQueue(Id(NetworkLane.WorldState), new byte[fragmentPayload + 1]));
    }

    private static void RetainedPayloadPressureIsExact()
    {
        using ProtocolLoopback loopback = Create();
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.Bulk), new byte[1_500]));
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.WorldState), new byte[100]));
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.OwnerState), new byte[50]));
        NetworkRetainedPayloadSnapshot queued = loopback.Client.GetSnapshot().RetainedPayloads;
        Equal(1_500, queued.ReliableBytes);
        Equal(100, queued.IndependentBytes);
        Equal(50, queued.UnreliableStreamBytes);
        Equal(1_650L, queued.TotalBytes);
        Equal(1_500, queued.LargestRetainedMessageBytes);

        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.OwnerState), new byte[25]));
        NetworkRetainedPayloadSnapshot replaced = loopback.Client.GetSnapshot().RetainedPayloads;
        Equal(25, replaced.UnreliableStreamBytes);
        Equal(50, replaced.UnreliableStreamHighWaterBytes);

        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 3 &&
            loopback.Client.GetSnapshot().ReliableQueued == 0, 10_000);
        NetworkRetainedPayloadSnapshot sender = loopback.Client.GetSnapshot().RetainedPayloads;
        NetworkRetainedPayloadSnapshot receiver = loopback.Server.GetSnapshot().RetainedPayloads;
        Equal(0, sender.ReliableBytes);
        Equal(0, sender.IndependentBytes);
        Equal(0, sender.UnreliableStreamBytes);
        Equal(0, receiver.ReassemblyBytes);
        Equal(1_500, receiver.ReassemblyHighWaterBytes);
        Equal(1_625, receiver.ReceivedBytes);
        Equal(1_625, receiver.ReceivedHighWaterBytes);
        Equal(1_500, receiver.LargestRetainedMessageBytes);
        Equal(0UL, receiver.ByteCapacityFailures);

        while (loopback.Server.TryConsumeReceived()) { }
        Equal(0, loopback.Server.GetSnapshot().RetainedPayloads.ReceivedBytes);
    }

    private static void RetainedPayloadByteBudgetsRejectAndCompactTransactionally()
    {
        NetworkProtocolLimits limits = s_limits with
        {
            MaximumReliableMessages = 4,
            MaximumReceivedMessages = 4,
            ReliablePayloadByteCapacity = 4_096,
            ReceivedPayloadByteCapacity = 4_096,
        };
        using (ProtocolLoopback capacity = Create(default, s_datagrams, limits))
        {
            Equal(4_096, capacity.Client.GetSnapshot().RetainedPayloads.ReliableCapacityBytes);
            Equal(NetworkQueueStatus.Accepted,
                capacity.Client.TryQueue(Id(NetworkLane.Bulk), new byte[2_000]));
            Equal(NetworkQueueStatus.Accepted,
                capacity.Client.TryQueue(Id(NetworkLane.Bulk), new byte[2_000]));
            Equal(NetworkQueueStatus.CapacityExceeded,
                capacity.Client.TryQueue(Id(NetworkLane.Bulk), new byte[100]));
            Equal(2, capacity.Client.GetSnapshot().ReliableQueued);
            Equal(1UL, capacity.Client.GetSnapshot().RetainedPayloads.ByteCapacityFailures);
        }

        using (ProtocolLoopback receivePressure = Create(default, s_datagrams, limits))
        {
            byte[] retained = new byte[2_000];
            Equal(NetworkQueueStatus.Accepted,
                receivePressure.Client.TryQueue(Id(NetworkLane.Bulk), retained));
            Equal(NetworkQueueStatus.Accepted,
                receivePressure.Client.TryQueue(Id(NetworkLane.Bulk), retained));
            Drive(receivePressure, () => receivePressure.Server.GetSnapshot().ReceivedQueued == 2 &&
                receivePressure.Client.GetSnapshot().ReliableQueued == 0, 10_000);
            Equal(NetworkQueueStatus.Accepted,
                receivePressure.Client.TryQueue(Id(NetworkLane.Bulk), new byte[100]));
            for (int pump = 0; pump < 32; pump++) receivePressure.Pump();
            Equal(1, receivePressure.Client.GetSnapshot().ReliableQueued);
            True(receivePressure.Server.GetSnapshot().RetainedPayloads.ByteCapacityFailures > 0);
            AssertReceive(receivePressure.Server, NetworkLane.Bulk, retained);
            Drive(receivePressure, () => receivePressure.Server.GetSnapshot().ReceivedQueued == 2 &&
                receivePressure.Client.GetSnapshot().ReliableQueued == 0, 10_000);
        }

        using ProtocolLoopback compacted = Create(default, s_datagrams, limits);
        byte[] first = Enumerable.Repeat((byte)1, 1_000).ToArray();
        byte[] second = Enumerable.Repeat((byte)2, 1_000).ToArray();
        byte[] third = Enumerable.Repeat((byte)3, 1_000).ToArray();
        byte[] fourth = Enumerable.Repeat((byte)4, 1_500).ToArray();
        Equal(NetworkQueueStatus.Accepted, compacted.Client.TryQueue(Id(NetworkLane.Bulk), first));
        Equal(NetworkQueueStatus.Accepted, compacted.Client.TryQueue(Id(NetworkLane.Bulk), second));
        Equal(NetworkQueueStatus.Accepted, compacted.Client.TryQueue(Id(NetworkLane.Bulk), third));
        Drive(compacted, () => compacted.Server.GetSnapshot().ReceivedQueued == 3 &&
            compacted.Client.GetSnapshot().ReliableQueued == 0, 10_000);
        AssertReceive(compacted.Server, NetworkLane.Bulk, first);
        Func<bool> compactedDeliveryComplete = () =>
            compacted.Server.GetSnapshot().ReceivedQueued == 3 &&
            compacted.Client.GetSnapshot().ReliableQueued == 0;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Equal(NetworkQueueStatus.Accepted, compacted.Client.TryQueue(Id(NetworkLane.Bulk), fourth));
        Drive(compacted, compactedDeliveryComplete, 10_000);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        AssertReceive(compacted.Server, NetworkLane.Bulk, second);
        AssertReceive(compacted.Server, NetworkLane.Bulk, third);
        AssertReceive(compacted.Server, NetworkLane.Bulk, fourth);
        Equal(0UL, compacted.Server.GetSnapshot().RetainedPayloads.ByteCapacityFailures);
        True(compacted.Server.GetSnapshot().RetainedPayloads.ReceivedCompactions >= 1);
    }

    private static void InboundBudgetCannotStarveOutboundControl()
    {
        NetworkProtocolLimits limits = s_limits with { MaximumInboundPacketsPerPump = 2 };
        var link = new DeterministicDatagramLink(s_datagrams);
        byte[] secret = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        using var receiver = new NetworkProtocolPeer(
            link.B, s_manifest, limits, s_datagrams, new ConnectionId(9, 1),
            new SecurityEpoch(1), secret, sendsClientToServer: false);
        CryptographicOperations.ZeroMemory(secret);
        ReadOnlySpan<byte> malformed = stackalloc byte[] { 0xFF };
        for (int index = 0; index < 10; index++)
            Equal(DatagramSendStatus.Accepted, link.A.TrySend(malformed));
        Equal(NetworkQueueStatus.Accepted,
            receiver.TryQueue(Id(NetworkLane.Control), stackalloc byte[] { 7 }));

        receiver.ProcessInbound(0);
        Equal(2UL, receiver.GetSnapshot().AuthenticationFailures);
        Equal(8, link.GetSnapshot().PendingAToB);
        receiver.Emit(0);
        byte[] outbound = new byte[s_datagrams.MaximumDatagramBytes];
        True(link.A.TryReceive(outbound, out _));
    }

    private static void WarmedSecureDeliveryAllocatesZero()
    {
        using ProtocolLoopback loopback = Create();
        Span<byte> payload = stackalloc byte[8];
        WriteUInt64(payload, 1);
        Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.OwnerState), payload));
        Drive(loopback, () => loopback.Server.GetSnapshot().ReceivedQueued == 1, 100);
        True(loopback.Server.TryConsumeReceived());
        for (int warm = 0; warm < 16; warm++) loopback.Pump();

        long before = GC.GetAllocatedBytesForCurrentThread();
        ulong checksum = 0;
        for (ulong iteration = 0; iteration < 1_000; iteration++)
        {
            WriteUInt64(payload, iteration);
            Equal(NetworkQueueStatus.Accepted, loopback.Client.TryQueue(Id(NetworkLane.OwnerState), payload));
            for (int pump = 0; pump < 4 && loopback.Server.GetSnapshot().ReceivedQueued == 0; pump++) loopback.Pump();
            True(loopback.Server.TryPeekReceived(out _, out ReadOnlySpan<byte> delivered));
            checksum += ReadUInt64(delivered);
            True(loopback.Server.TryConsumeReceived());
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated);
        Equal(499_500UL, checksum);

        before = GC.GetAllocatedBytesForCurrentThread();
        checksum = 0;
        for (ulong iteration = 0; iteration < 1_000; iteration++)
        {
            WriteUInt64(payload, iteration);
            Equal(NetworkQueueStatus.Accepted,
                loopback.Client.TryQueue(Id(NetworkLane.WorldState), payload));
            for (int pump = 0; pump < 4 && loopback.Server.GetSnapshot().ReceivedQueued == 0; pump++)
                loopback.Pump();
            True(loopback.Server.TryPeekReceived(out _, out ReadOnlySpan<byte> delivered));
            checksum += ReadUInt64(delivered);
            True(loopback.Server.TryConsumeReceived());
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated);
        Equal(499_500UL, checksum);
    }

    private static void AbortiveShutdownIsBalancedAndIdempotent()
    {
        ProtocolLoopback loopback = Create();
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(Id(NetworkLane.Bulk), new byte[2_000]));
        loopback.Pump();
        loopback.Dispose();
        loopback.Dispose();
        Throws<ObjectDisposedException>(() => loopback.Pump());
        Throws<ObjectDisposedException>(() => loopback.Client.TryQueue(Id(NetworkLane.Control), default));
    }

    private static ProtocolLoopback Create(
        NetworkImpairmentOptions impairment = default,
        DeterministicDatagramLimits? datagrams = null,
        NetworkProtocolLimits? limits = null)
    {
        True(ProtocolLoopback.TryCreate(limits ?? s_limits, datagrams ?? s_datagrams, impairment,
            out ProtocolLoopback? loopback, out _, s_manifest));
        return loopback!;
    }

    private static NetworkProtocolManifest CreateManifest()
    {
        var descriptors = new NetworkMessageDescriptor[NetworkLaneContracts.Count];
        foreach (NetworkLane lane in Enum.GetValues<NetworkLane>())
            descriptors[(int)lane] = new(new NetworkMessageId((ushort)((int)lane + 1)),
                $"n0.3.test.{lane}", 1, lane, 4_096, (ulong)lane + 100);
        return NetworkProtocolManifest.Create(3, descriptors);
    }

    private static NetworkMessageId Id(NetworkLane lane) => new((ushort)((int)lane + 1));

    private static int Drain(DeterministicDatagramEndpoint endpoint)
    {
        byte[] scratch = new byte[s_datagrams.MaximumDatagramBytes];
        int count = 0;
        while (endpoint.TryReceive(scratch, out _)) count++;
        return count;
    }

    private static void Drive(RawPeerPair pair, ulong firstStep, Func<bool> complete, int maximumSteps)
    {
        for (ulong step = firstStep; step < firstStep + unchecked((ulong)maximumSteps); step++)
        {
            if (complete()) return;
            pair.Sender.ProcessInbound(step);
            pair.Receiver.ProcessInbound(step);
            pair.Sender.Emit(step);
            pair.Receiver.Emit(step);
            pair.Link.Advance();
        }
        throw new InvalidOperationException("Raw protocol peers did not converge.");
    }

    private static void Drive(ProtocolLoopback loopback, Func<bool> complete, int maximumSteps)
    {
        for (int step = 0; step < maximumSteps; step++)
        {
            if (complete()) return;
            loopback.Pump();
        }
        throw new InvalidOperationException($"Protocol did not converge in {maximumSteps} steps. Snapshot: {loopback.GetSnapshot()}.");
    }

    private static void AssertReceive(NetworkProtocolPeer peer, NetworkLane expectedLane, ReadOnlySpan<byte> expected)
    {
        True(peer.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload));
        Equal(expectedLane, message.Lane);
        True(payload.SequenceEqual(expected));
        True(peer.TryConsumeReceived());
    }

    private static void WriteUInt64(Span<byte> destination, ulong value)
    {
        var writer = new NetworkPacketWriter(destination);
        True(writer.TryWriteUInt64(value));
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> source)
    {
        var reader = new NetworkPacketReader(source);
        True(reader.TryReadUInt64(out ulong value));
        True(reader.IsComplete);
        return value;
    }

    private static void Run(string name, Action body)
    {
        body();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool condition) => True(!condition);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class RawPeerPair : IDisposable
    {
        private readonly byte[] _masterSecret;
        private readonly ConnectionId _connection = new(13, 5);
        private readonly SecurityEpoch _epoch = new(1);
        private readonly NetworkProtocolManifest _manifest;

        internal RawPeerPair(
            NetworkProtocolManifest manifest,
            NetworkProtocolLimits limits,
            DeterministicDatagramLimits datagrams)
        {
            _manifest = manifest;
            _masterSecret = Enumerable.Range(1, 32).Select(static value => (byte)(value * 3)).ToArray();
            Link = new DeterministicDatagramLink(datagrams);
            Sender = new NetworkProtocolPeer(
                Link.A, manifest, limits, datagrams, _connection, _epoch, _masterSecret,
                sendsClientToServer: true);
            Receiver = new NetworkProtocolPeer(
                Link.B, manifest, limits, datagrams, _connection, _epoch, _masterSecret,
                sendsClientToServer: false);
        }

        internal DeterministicDatagramLink Link { get; }
        internal NetworkProtocolPeer Sender { get; }
        internal NetworkProtocolPeer Receiver { get; }

        internal void InjectData(
            NetworkMessageId messageId,
            NetworkLane lane,
            ulong sequence,
            ulong packet,
            ReadOnlySpan<byte> payload)
        {
            True(_manifest.TryGet(messageId, out NetworkMessageDescriptor descriptor));
            Span<byte> plaintext = stackalloc byte[NetworkProtocolDataHeader.EncodedBytes + payload.Length];
            var writer = new NetworkPacketWriter(plaintext);
            var header = new NetworkDataFrameHeader(
                lane, messageId, descriptor.Version, new LaneSequence(sequence),
                0, 1, payload.Length, checked((ushort)payload.Length));
            True(NetworkDataFrameCodec.TryWrite(ref writer, header));
            True(writer.TryWriteBytes(payload));
            byte[] datagram = CreateAuthenticatedDatagram(
                _masterSecret, _connection, _epoch, new PacketNumber(packet), writer.WrittenSpan);
            Equal(DatagramSendStatus.Accepted, Link.A.TrySend(datagram));
            Receiver.ProcessInbound(packet);
        }

        public void Dispose()
        {
            Sender.Dispose();
            Receiver.Dispose();
            CryptographicOperations.ZeroMemory(_masterSecret);
        }
    }
}
