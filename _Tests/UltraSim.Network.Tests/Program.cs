using System.Net;
using System.Reflection;
using UltraSim.Network;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(NetworkPlane_PreservesExpectedValues), NetworkPlane_PreservesExpectedValues);
        Run(nameof(PacketClass_PreservesExpectedValues), PacketClass_PreservesExpectedValues);
        Run(nameof(NetworkServiceClass_PreservesExpectedValues), NetworkServiceClass_PreservesExpectedValues);
        Run(nameof(ChannelType_PreservesExpectedValues), ChannelType_PreservesExpectedValues);
        Run(nameof(ImmutablePayloadBuffer_CopyFrom_CreatesImmutableSnapshot), ImmutablePayloadBuffer_CopyFrom_CreatesImmutableSnapshot);
        Run(nameof(OutboundTransformContext_PreservesSuppliedFlags), OutboundTransformContext_PreservesSuppliedFlags);
        Run(nameof(INetworkTransport_ExposesExpectedSurface), INetworkTransport_ExposesExpectedSurface);
        Run(nameof(IOutboundTransform_ExposesExplicitTransformHook), IOutboundTransform_ExposesExplicitTransformHook);
        Run(nameof(PacketEnvelopeHeader_RoundTripsExpectedFields), PacketEnvelopeHeader_RoundTripsExpectedFields);
        Run(nameof(PacketCodec_EncodesAndDecodesHandshakeHello), PacketCodec_EncodesAndDecodesHandshakeHello);
        Run(nameof(ServerDisconnectPacket_RoundTripsReason), ServerDisconnectPacket_RoundTripsReason);
        Run(nameof(ClientDisconnectPacket_RoundTripsReason), ClientDisconnectPacket_RoundTripsReason);
        Run(nameof(ChatSubmitPacket_RoundTripsSubmittedText), ChatSubmitPacket_RoundTripsSubmittedText);
        Run(nameof(ChatMessageEventPacket_RoundTripsMessageMetadata), ChatMessageEventPacket_RoundTripsMessageMetadata);
        Run(nameof(UiPanelOpenEventPacket_RoundTripsDocumentBytes), UiPanelOpenEventPacket_RoundTripsDocumentBytes);
        Run(nameof(UiModelUpdateEventPacket_RoundTripsDocumentBytes), UiModelUpdateEventPacket_RoundTripsDocumentBytes);
        Run(nameof(UiDocumentChunkEventPacket_RoundTripsChunkMetadataAndBytes), UiDocumentChunkEventPacket_RoundTripsChunkMetadataAndBytes);
        Run(nameof(UiActionRequestPacket_RoundTripsDocumentBytes), UiActionRequestPacket_RoundTripsDocumentBytes);
        Run(nameof(LoginCredentialSubmitPacket_RoundTripsTrustedCredentialMaterial), LoginCredentialSubmitPacket_RoundTripsTrustedCredentialMaterial);
        Run(nameof(RealtimeStateBatchPacket_RoundTripsExpectedItems), RealtimeStateBatchPacket_RoundTripsExpectedItems);
        Run(nameof(PacketRegistry_DispatchesTypedPacketAndMetadata), PacketRegistry_DispatchesTypedPacketAndMetadata);
        Run(nameof(PacketMetadataRegistry_ResolvesRegisteredPacketMetadata), PacketMetadataRegistry_ResolvesRegisteredPacketMetadata);
        Run(nameof(HandshakeGate_AcceptsAndRejectsExpectedReasons), HandshakeGate_AcceptsAndRejectsExpectedReasons);
        Run(nameof(NetworkSessionManager_CreatesTransitionsAndDrainsLifecycleEvents), NetworkSessionManager_CreatesTransitionsAndDrainsLifecycleEvents);
        Run(nameof(NetworkSessionManager_AdmissionPolicy_RejectsAndReplacesDuplicates), NetworkSessionManager_AdmissionPolicy_RejectsAndReplacesDuplicates);
        Run(nameof(NetworkSessionManager_SweepTimeouts_DisconnectsExpiredSessions), NetworkSessionManager_SweepTimeouts_DisconnectsExpiredSessions);
        Run(nameof(NetworkSessionManager_MetricsSnapshot_TracksRejectsTimeoutsAndPeak), NetworkSessionManager_MetricsSnapshot_TracksRejectsTimeoutsAndPeak);
        Run(nameof(ControlPlane_LoopbackHandshake_RoundTripsOverTransport), ControlPlane_LoopbackHandshake_RoundTripsOverTransport);
        Run(nameof(ControlPlane_UdpHandshake_RoundTripsOverTransport), ControlPlane_UdpHandshake_RoundTripsOverTransport);
        Run(nameof(ControlPlane_RetryClientHandshake_ReSendsHelloWithoutDuplicateLocalSession), ControlPlane_RetryClientHandshake_ReSendsHelloWithoutDuplicateLocalSession);
        Run(nameof(ControlPlane_LoopbackHandshake_RejectsInvalidHello), ControlPlane_LoopbackHandshake_RejectsInvalidHello);
        Run(nameof(ControlPlane_AdmissionReject_ReportsReasonAndMetrics), ControlPlane_AdmissionReject_ReportsReasonAndMetrics);
        Run(nameof(ControlPlane_PingProbe_RoundTripsAfterReadyHandshake), ControlPlane_PingProbe_RoundTripsAfterReadyHandshake);
        Run(nameof(ControlPlane_ServerDisconnectNotice_DisconnectsClientSession), ControlPlane_ServerDisconnectNotice_DisconnectsClientSession);
        Run(nameof(ControlPlane_ClientDisconnectNotice_DisconnectsServerSession), ControlPlane_ClientDisconnectNotice_DisconnectsServerSession);
        Run(nameof(NetworkSessionManager_SessionIdExhaustion_RejectsGracefully), NetworkSessionManager_SessionIdExhaustion_RejectsGracefully);
        Run(nameof(LatestStateStream_CoalescesAndDrainsByLatestVersion), LatestStateStream_CoalescesAndDrainsByLatestVersion);
        Run(nameof(LatestStateStream_PublishScopedCopies_PreservesSharedItemKeyAcrossScopes), LatestStateStream_PublishScopedCopies_PreservesSharedItemKeyAcrossScopes);
        Run(nameof(LatestStateStream_PartialDrain_AdvancesCursor), LatestStateStream_PartialDrain_AdvancesCursor);
        Run(nameof(LatestStateStream_CopySnapshot_ReturnsAllUpdatesSorted), LatestStateStream_CopySnapshot_ReturnsAllUpdatesSorted);
        Run(nameof(LatestStateStream_CopySnapshotForScopes_FiltersAndPreservesSequenceOrder), LatestStateStream_CopySnapshotForScopes_FiltersAndPreservesSequenceOrder);
        Run(nameof(ConnectionLatestStateSentTracker_TracksStableItemIdentityPerConnection), ConnectionLatestStateSentTracker_TracksStableItemIdentityPerConnection);
        Run(nameof(ConnectionLatestStateDeltaPlanner_FiltersSnapshotAgainstLastSentPerConnection), ConnectionLatestStateDeltaPlanner_FiltersSnapshotAgainstLastSentPerConnection);
        Run(nameof(OrderedEventStream_DrainsInOrderAndAdvancesCursor), OrderedEventStream_DrainsInOrderAndAdvancesCursor);
        Run(nameof(OrderedEventStream_RequiresRecoveryWhenCursorFallsBehind), OrderedEventStream_RequiresRecoveryWhenCursorFallsBehind);
        Run(nameof(ReliableEventReplayBuffer_TracksAcknowledgesAndReplaysInOrder), ReliableEventReplayBuffer_TracksAcknowledgesAndReplaysInOrder);
        Run(nameof(ReliableEventReplayBuffer_RequiresRecoveryWhenRequestedSequenceFallsBehindWindow), ReliableEventReplayBuffer_RequiresRecoveryWhenRequestedSequenceFallsBehindWindow);
        Run(nameof(ReliableEventRetransmitPlanner_WaitsForThresholdThenReturnsBoundedReplaySet), ReliableEventRetransmitPlanner_WaitsForThresholdThenReturnsBoundedReplaySet);
        Run(nameof(ReliableEventRetransmitPlanner_RequestsRecoveryWhenAckFallsBehindReplayWindow), ReliableEventRetransmitPlanner_RequestsRecoveryWhenAckFallsBehindReplayWindow);
        Run(nameof(ReliableEventRetransmitBuilder_BuildsOrderedEventDatagrams), ReliableEventRetransmitBuilder_BuildsOrderedEventDatagrams);
        Run(nameof(StreamPrimitives_PreserveBulkPacketClassMetadata), StreamPrimitives_PreserveBulkPacketClassMetadata);
        Run(nameof(ConnectionStreamScheduler_PrioritizesRealtimeThenReliableWithinBudget), ConnectionStreamScheduler_PrioritizesRealtimeThenReliableWithinBudget);
        Run(nameof(ConnectionStreamScheduler_SchedulesBulkAgainstBulkBudget), ConnectionStreamScheduler_SchedulesBulkAgainstBulkBudget);
        Run(nameof(ConnectionStreamScheduler_LeavesCursorsOnUnscheduledWork), ConnectionStreamScheduler_LeavesCursorsOnUnscheduledWork);
        Run(nameof(ConnectionStreamScheduler_SignalsOrderedEventRecovery), ConnectionStreamScheduler_SignalsOrderedEventRecovery);
        Run(nameof(OutboundDatagramAssembler_EncodesScheduledWorkIntoTypedFrames), OutboundDatagramAssembler_EncodesScheduledWorkIntoTypedFrames);
        Run(nameof(OutboundDatagramAssembler_AppliesTransformsAndHeaderFlags), OutboundDatagramAssembler_AppliesTransformsAndHeaderFlags);
        Run(nameof(ConnectionSubscriptionRegistry_TracksSubscriptionsAndRemovals), ConnectionSubscriptionRegistry_TracksSubscriptionsAndRemovals);
        Run(nameof(SharedFramePublisher_CreatesTypedPublishedFrames), SharedFramePublisher_CreatesTypedPublishedFrames);
        Run(nameof(StateBaselineSnapshotBuilder_EmitsBeginFramesAndComplete), StateBaselineSnapshotBuilder_EmitsBeginFramesAndComplete);
        Run(nameof(ConnectionRecoveryPlanner_UsesSchedulerWhenNoRecoveryIsRequired), ConnectionRecoveryPlanner_UsesSchedulerWhenNoRecoveryIsRequired);
        Run(nameof(ConnectionRecoveryPlanner_BuildsFullBaselineWhenOrderedEventRecoveryIsRequired), ConnectionRecoveryPlanner_BuildsFullBaselineWhenOrderedEventRecoveryIsRequired);
        Run(nameof(ConnectionRecoveryExecutionBridge_RetransmitsBufferedEntriesWithinWindow), ConnectionRecoveryExecutionBridge_RetransmitsBufferedEntriesWithinWindow);
        Run(nameof(ConnectionRecoveryExecutionBridge_FallsBackToFullBaselineWhenReplayWindowIsBehind), ConnectionRecoveryExecutionBridge_FallsBackToFullBaselineWhenReplayWindowIsBehind);
        Run(nameof(ConnectionLatestStateDeltaExecutionBridge_EmitsDeltaFramesAndMarksSentState), ConnectionLatestStateDeltaExecutionBridge_EmitsDeltaFramesAndMarksSentState);
        Run(nameof(ScheduledWorkPublishedFrameProjector_ProjectsScopedFrames), ScheduledWorkPublishedFrameProjector_ProjectsScopedFrames);
        Run(nameof(OutboundDatagramAssembler_Fanout_AssemblesSharedFrameForSubscribers), OutboundDatagramAssembler_Fanout_AssemblesSharedFrameForSubscribers);
        Run(nameof(SubscriptionFanoutDispatcher_DispatchesScopedFramesToSubscribers), SubscriptionFanoutDispatcher_DispatchesScopedFramesToSubscribers);
        Run(nameof(SubscriptionFanoutDispatcher_DispatchScheduledWork_ToSubscribers), SubscriptionFanoutDispatcher_DispatchScheduledWork_ToSubscribers);
        Run(nameof(SubscriptionFanoutDispatcher_DispatchScheduledWork_AppliesLatestStateRecipientPhasing), SubscriptionFanoutDispatcher_DispatchScheduledWork_AppliesLatestStateRecipientPhasing);
        Run(nameof(SubscriptionFanoutDispatcher_DispatchScheduledWork_AppliesLatestStateConnectionPhasing), SubscriptionFanoutDispatcher_DispatchScheduledWork_AppliesLatestStateConnectionPhasing);
        Run(nameof(SubscriptionFanoutDispatcher_DispatchScheduledWork_BatchesContiguousRealtimeLatestStateItems), SubscriptionFanoutDispatcher_DispatchScheduledWork_BatchesContiguousRealtimeLatestStateItems);
        Run(nameof(ScheduledWorkFanoutBridge_ProjectsAndDispatchesInOnePass), ScheduledWorkFanoutBridge_ProjectsAndDispatchesInOnePass);
        Run(nameof(ConnectionStreamFanoutBridge_SchedulesAndDispatchesScopedStreamWork), ConnectionStreamFanoutBridge_SchedulesAndDispatchesScopedStreamWork);
        Run(nameof(ChunkAoiPolicy_AssignsUniqueNearestRingsByWorldSpaceOverlap), ChunkAoiPolicy_AssignsUniqueNearestRingsByWorldSpaceOverlap);
        Run(nameof(ChunkAoiSubscriptionMaintainer_ReconcilesSubscriptionsAcrossMovement), ChunkAoiSubscriptionMaintainer_ReconcilesSubscriptionsAcrossMovement);
        Run(nameof(LoopbackTransportPair_RoundTripsDatagrams), LoopbackTransportPair_RoundTripsDatagrams);
        Run(nameof(LoopbackTransport_Stop_BlocksFurtherPollOutput), LoopbackTransport_Stop_BlocksFurtherPollOutput);
        Run(nameof(UdpSocketTransport_FlushOutbound_BundlesSameConnectionPackets), UdpSocketTransport_FlushOutbound_BundlesSameConnectionPackets);
        Run(nameof(UdpSocketTransport_FlushOutbound_RejectsOversizedSingleEnvelopeWithCapacityDetail), UdpSocketTransport_FlushOutbound_RejectsOversizedSingleEnvelopeWithCapacityDetail);
        Run(nameof(UdpSocketTransport_AsyncFlushOutbound_BundlesSameConnectionPackets), UdpSocketTransport_AsyncFlushOutbound_BundlesSameConnectionPackets);
        Run(nameof(UdpSocketTransport_AsyncReceive_RoundTripsDatagrams), UdpSocketTransport_AsyncReceive_RoundTripsDatagrams);
        Run(nameof(UdpSocketTransport_RoundTripsDatagrams), UdpSocketTransport_RoundTripsDatagrams);

        Console.WriteLine("All Network scaffold tests passed.");
    }

    private static void NetworkPlane_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)NetworkPlane.Control);
        Assert.Equal(1, (int)NetworkPlane.Input);
        Assert.Equal(2, (int)NetworkPlane.State);
    }

    private static void PacketClass_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)PacketClass.Control);
        Assert.Equal(1, (int)PacketClass.Realtime);
        Assert.Equal(2, (int)PacketClass.ReliableEvent);
        Assert.Equal(3, (int)PacketClass.Bulk);
    }

    private static void NetworkServiceClass_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)NetworkServiceClass.A);
        Assert.Equal(1, (int)NetworkServiceClass.B);
        Assert.Equal(2, (int)NetworkServiceClass.C);
        Assert.Equal(3, (int)NetworkServiceClass.D);
    }

    private static void ChannelType_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)ChannelType.ReliableOrdered);
        Assert.Equal(1, (int)ChannelType.ReliableUnordered);
        Assert.Equal(2, (int)ChannelType.UnreliableSequenced);
        Assert.Equal(3, (int)ChannelType.Unreliable);
    }

    private static void ImmutablePayloadBuffer_CopyFrom_CreatesImmutableSnapshot()
    {
        byte[] source = [1, 2, 3];
        ImmutablePayloadBuffer payload = ImmutablePayloadBuffer.CopyFrom(source);

        source[0] = 99;

        Assert.Equal(3, payload.Length);
        Assert.Equal((byte)1, payload.Span[0]);
        Assert.Equal((byte)2, payload.Span[1]);
        Assert.Equal((byte)3, payload.Span[2]);
    }

    private static void OutboundTransformContext_PreservesSuppliedFlags()
    {
        var context = new OutboundTransformContext(
            new ConnectionId(7),
            NetworkPlane.State,
            PacketClass.Bulk,
            ChannelType.ReliableOrdered,
            AllowCompression: true,
            AllowSegmentation: false);

        Assert.Equal(7, context.ConnectionId.Value);
        Assert.Equal(NetworkPlane.State, context.Plane);
        Assert.Equal(PacketClass.Bulk, context.PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, context.Channel);
        Assert.True(context.AllowCompression);
        Assert.False(context.AllowSegmentation);
    }

    private static void INetworkTransport_ExposesExpectedSurface()
    {
        Type contract = typeof(INetworkTransport);

        Assert.True(contract.IsInterface);
        AssertMethod(contract, nameof(INetworkTransport.Start), typeof(void));
        AssertMethod(contract, nameof(INetworkTransport.Stop), typeof(void));
        AssertMethod(contract, nameof(INetworkTransport.PollInbound), typeof(int), typeof(Span<InboundDatagram>));
        AssertMethod(contract, nameof(INetworkTransport.EnqueueOutbound), typeof(void), typeof(OutboundDatagram).MakeByRefType());
        AssertMethod(contract, nameof(INetworkTransport.FlushOutbound), typeof(void));
    }

    private static void IOutboundTransform_ExposesExplicitTransformHook()
    {
        Type contract = typeof(IOutboundTransform);
        Assert.True(contract.IsInterface);
        AssertMethod(
            contract,
            nameof(IOutboundTransform.Transform),
            typeof(OutboundTransformResult),
            typeof(ImmutablePayloadBuffer),
            typeof(PacketHeaderFlags),
            typeof(OutboundTransformContext).MakeByRefType());
    }

    private static void LoopbackTransportPair_RoundTripsDatagrams()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        ImmutablePayloadBuffer payload = ImmutablePayloadBuffer.CopyFrom([7, 8, 9]);
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(42),
            NetworkPlane.Control,
            PacketClass.Control,
            ChannelType.ReliableOrdered,
            payload));
        pair.Server.FlushOutbound();

        var buffer = new InboundDatagram[4];
        int count = pair.Client.PollInbound(buffer);

        Assert.Equal(1, count);
        Assert.Equal(42, buffer[0].ConnectionId.Value);
        Assert.Equal(NetworkPlane.Control, buffer[0].Plane);
        Assert.Equal(PacketClass.Control, buffer[0].PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, buffer[0].Channel);
        Assert.True(buffer[0].Payload.Span.SequenceEqual(payload.Span));
    }

    private static void LoopbackTransport_Stop_BlocksFurtherPollOutput()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(9),
            NetworkPlane.State,
            PacketClass.Realtime,
            ChannelType.UnreliableSequenced,
            ImmutablePayloadBuffer.CopyFrom([1, 2])));
        pair.Server.FlushOutbound();
        pair.Client.Stop();

        var buffer = new InboundDatagram[2];
        Assert.Equal(0, pair.Client.PollInbound(buffer));
    }

    private static void UdpSocketTransport_RoundTripsDatagrams()
    {
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        using var server = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata));
        server.Start();

        var serverEndPoint = (IPEndPoint)Assert.NotNull(server.LocalEndPoint);
        using var client = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            serverEndPoint,
            new ConnectionId(1),
            AllowDynamicConnections: false));
        client.Start();

        ImmutablePayloadBuffer payload = PacketCodec.Encode(new PingProbePacket(sequence: 7, clientTick: 1234));
        client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            NetworkPlane.Control,
            PacketClass.Control,
            ChannelType.ReliableOrdered,
            payload));
        client.FlushOutbound();

        var inbound = new InboundDatagram[4];
        int count = PollUntilReceived(server, inbound, maxAttempts: 256);

        Assert.Equal(1, count);
        Assert.Equal(1, inbound[0].ConnectionId.Value);
        Assert.Equal(NetworkPlane.Control, inbound[0].Plane);
        Assert.Equal(PacketClass.Control, inbound[0].PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, inbound[0].Channel);
        Assert.True(PacketCodec.TryDecodeEnvelope(inbound[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> rawPayload));
        Assert.Equal(PingProbePacket.PacketId, header.PacketId);
        var reader = new BitReader(rawPayload);
        PingProbePacket packet = PingProbePacket.Deserialize(ref reader);
        Assert.Equal((ushort)7, packet.Sequence);
        Assert.Equal((uint)1234, packet.ClientTick);
    }

    private static void UdpSocketTransport_FlushOutbound_BundlesSameConnectionPackets()
    {
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        using var server = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            MaxDatagramBytes: 512));
        server.Start();

        var serverEndPoint = (IPEndPoint)Assert.NotNull(server.LocalEndPoint);
        using var client = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            serverEndPoint,
            new ConnectionId(1),
            AllowDynamicConnections: false,
            MaxDatagramBytes: 512));
        client.Start();

        for (ushort i = 1; i <= 3; i++)
        {
            ImmutablePayloadBuffer payload = PacketCodec.Encode(new PingProbePacket(i, (uint)(i * 10)));
            client.EnqueueOutbound(new OutboundDatagram(
                new ConnectionId(1),
                NetworkPlane.Control,
                PacketClass.Control,
                ChannelType.ReliableOrdered,
                payload));
        }

        client.FlushOutbound();

        Assert.Equal(1, client.LastFlushWireDatagramCount);

        var inbound = new InboundDatagram[8];
        int count = PollUntilReceived(server, inbound, maxAttempts: 256);

        Assert.Equal(3, count);
        for (int i = 0; i < count; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(inbound[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> rawPayload));
            Assert.Equal(PingProbePacket.PacketId, header.PacketId);
            var reader = new BitReader(rawPayload);
            PingProbePacket packet = PingProbePacket.Deserialize(ref reader);
            Assert.Equal((ushort)(i + 1), packet.Sequence);
            Assert.Equal((uint)((i + 1) * 10), packet.ClientTick);
        }
    }

    private static void UdpSocketTransport_FlushOutbound_RejectsOversizedSingleEnvelopeWithCapacityDetail()
    {
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        using var server = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            MaxDatagramBytes: 64));
        server.Start();

        var serverEndPoint = (IPEndPoint)Assert.NotNull(server.LocalEndPoint);
        using var client = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            serverEndPoint,
            new ConnectionId(1),
            AllowDynamicConnections: false,
            MaxDatagramBytes: 64));
        client.Start();

        client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            NetworkPlane.State,
            PacketClass.ReliableEvent,
            ChannelType.ReliableOrdered,
            ImmutablePayloadBuffer.CopyFrom(new byte[96])));

        try
        {
            client.FlushOutbound();
        }
        catch (InvalidOperationException ex)
        {
            Assert.True(ex.Message.Contains("exceeds transport datagram capacity", StringComparison.Ordinal));
            Assert.True(ex.Message.Contains("envelope=96 bytes", StringComparison.Ordinal));
            Assert.True(ex.Message.Contains("maxDatagram=64 bytes", StringComparison.Ordinal));
            return;
        }

        throw new InvalidOperationException("Expected oversized datagram flush to fail.");
    }

    private static void UdpSocketTransport_AsyncFlushOutbound_BundlesSameConnectionPackets()
    {
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        using var server = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            MaxDatagramBytes: 512));
        server.Start();

        var serverEndPoint = (IPEndPoint)Assert.NotNull(server.LocalEndPoint);
        using var client = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            serverEndPoint,
            new ConnectionId(1),
            AllowDynamicConnections: false,
            MaxDatagramBytes: 512,
            EnableAsyncFlush: true));
        client.Start();

        for (ushort i = 1; i <= 3; i++)
        {
            ImmutablePayloadBuffer payload = PacketCodec.Encode(new PingProbePacket(i, (uint)(i * 10)));
            client.EnqueueOutbound(new OutboundDatagram(
                new ConnectionId(1),
                NetworkPlane.Control,
                PacketClass.Control,
                ChannelType.ReliableOrdered,
                payload));
        }

        client.FlushOutbound();

        var inbound = new InboundDatagram[8];
        int count = PollUntilReceived(server, inbound, maxAttempts: 256);

        Assert.Equal(3, count);
        Assert.True(client.LastFlushProfile.AsyncEnabled);
        Assert.True(client.LastFlushProfile.AsyncStaged);
        for (int i = 0; i < count; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(inbound[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> rawPayload));
            Assert.Equal(PingProbePacket.PacketId, header.PacketId);
            var reader = new BitReader(rawPayload);
            PingProbePacket packet = PingProbePacket.Deserialize(ref reader);
            Assert.Equal((ushort)(i + 1), packet.Sequence);
            Assert.Equal((uint)((i + 1) * 10), packet.ClientTick);
        }
    }

    private static void UdpSocketTransport_AsyncReceive_RoundTripsDatagrams()
    {
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        using var server = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            EnableAsyncReceive: true));
        server.Start();

        var serverEndPoint = (IPEndPoint)Assert.NotNull(server.LocalEndPoint);
        using var client = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            serverEndPoint,
            new ConnectionId(1),
            AllowDynamicConnections: false));
        client.Start();

        ImmutablePayloadBuffer payload = PacketCodec.Encode(new PingProbePacket(sequence: 11, clientTick: 2222));
        client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            NetworkPlane.Control,
            PacketClass.Control,
            ChannelType.ReliableOrdered,
            payload));
        client.FlushOutbound();

        var inbound = new InboundDatagram[4];
        int count = PollUntilReceived(server, inbound, maxAttempts: 256);

        Assert.Equal(1, count);
        Assert.True(server.LastPollProfile.AsyncEnabled);
        Assert.True(server.LastPollProfile.AsyncQueuedDatagramCount >= 0);
        Assert.Equal(1, inbound[0].ConnectionId.Value);
        Assert.True(PacketCodec.TryDecodeEnvelope(inbound[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> rawPayload));
        Assert.Equal(PingProbePacket.PacketId, header.PacketId);
        var reader = new BitReader(rawPayload);
        PingProbePacket packet = PingProbePacket.Deserialize(ref reader);
        Assert.Equal((ushort)11, packet.Sequence);
        Assert.Equal((uint)2222, packet.ClientTick);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{name} failed: {ex}");
            Environment.Exit(1);
        }
    }

    private static void AssertMethod(Type contract, string name, Type returnType, params Type[] parameterTypes)
    {
        MethodInfo? method = contract.GetMethod(name, parameterTypes);
        Assert.NotNull(method);
        Assert.Equal(returnType, method!.ReturnType);
    }

    private static void PacketEnvelopeHeader_RoundTripsExpectedFields()
    {
        byte[] bytes = new byte[PacketEnvelopeHeader.SizeInBytes];
        var writer = new BitWriter(bytes);
        var header = new PacketEnvelopeHeader(0x1234, PacketHeaderFlags.Compressed | PacketHeaderFlags.HighPriorityHint, 77);
        header.Serialize(ref writer);

        var reader = new BitReader(bytes);
        PacketEnvelopeHeader decoded = PacketEnvelopeHeader.Deserialize(ref reader);

        Assert.Equal((ushort)0x1234, decoded.PacketId);
        Assert.Equal(PacketHeaderFlags.Compressed | PacketHeaderFlags.HighPriorityHint, decoded.Flags);
        Assert.Equal((ushort)77, decoded.PayloadLength);
    }

    private static void PacketCodec_EncodesAndDecodesHandshakeHello()
    {
        var hello = new HandshakeHelloPacket(
            protocolVersion: 7,
            schemaHash: 11,
            contentHash: 13,
            features: FeatureFlags.Compression | FeatureFlags.SharedPayloadFanout);
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(hello, PacketHeaderFlags.None);

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(HandshakeHelloPacket.PacketId, header.PacketId);
        Assert.Equal(PacketHeaderFlags.None, header.Flags);

        var reader = new BitReader(payload);
        HandshakeHelloPacket decoded = HandshakeHelloPacket.Deserialize(ref reader);
        Assert.Equal((uint)7, decoded.ProtocolVersion);
        Assert.Equal((ulong)11, decoded.SchemaHash);
        Assert.Equal((ulong)13, decoded.ContentHash);
        Assert.Equal(FeatureFlags.Compression | FeatureFlags.SharedPayloadFanout, decoded.Features);
    }

    private static void ServerDisconnectPacket_RoundTripsReason()
    {
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(new ServerDisconnectPacket(SessionDisconnectReason.ServerShutdown));

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(ServerDisconnectPacket.PacketId, header.PacketId);
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        Assert.True(metadata.TryGet(ServerDisconnectPacket.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel));
        Assert.Equal(NetworkPlane.Control, plane);
        Assert.Equal(PacketClass.Control, packetClass);
        Assert.Equal(ChannelType.ReliableOrdered, channel);

        var reader = new BitReader(payload);
        ServerDisconnectPacket decoded = ServerDisconnectPacket.Deserialize(ref reader);
        Assert.Equal(SessionDisconnectReason.ServerShutdown, decoded.Reason);
    }

    private static void ClientDisconnectPacket_RoundTripsReason()
    {
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(new ClientDisconnectPacket(SessionDisconnectReason.Closed));

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(ClientDisconnectPacket.PacketId, header.PacketId);
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        Assert.True(metadata.TryGet(ClientDisconnectPacket.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel));
        Assert.Equal(NetworkPlane.Control, plane);
        Assert.Equal(PacketClass.Control, packetClass);
        Assert.Equal(ChannelType.ReliableOrdered, channel);

        var reader = new BitReader(payload);
        ClientDisconnectPacket decoded = ClientDisconnectPacket.Deserialize(ref reader);
        Assert.Equal(SessionDisconnectReason.Closed, decoded.Reason);
    }

    private static void ChatSubmitPacket_RoundTripsSubmittedText()
    {
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(new ChatSubmitPacket("[add dagger"));

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(ChatSubmitPacket.PacketId, header.PacketId);
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        Assert.True(metadata.TryGet(ChatSubmitPacket.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel));
        Assert.Equal(NetworkPlane.Input, plane);
        Assert.Equal(PacketClass.ReliableEvent, packetClass);
        Assert.Equal(ChannelType.ReliableOrdered, channel);

        var reader = new BitReader(payload);
        ChatSubmitPacket decoded = ChatSubmitPacket.Deserialize(ref reader);
        Assert.Equal("[add dagger", decoded.Text);
    }

    private static void ChatMessageEventPacket_RoundTripsMessageMetadata()
    {
        var packet = new ChatMessageEventPacket(
            MessageId: 99,
            ChannelId: 2,
            SenderEntityId: 1234,
            SenderName: "server",
            Text: "Maintenance in 10 minutes.",
            ServerTick: 77,
            Flags: ChatMessageFlags.System | ChatMessageFlags.Announcement);

        ImmutablePayloadBuffer encoded = PacketCodec.EncodePayload(packet);
        var reader = new BitReader(encoded.Span);
        ChatMessageEventPacket decoded = ChatMessageEventPacket.Deserialize(ref reader);

        Assert.Equal((ulong)99, decoded.MessageId);
        Assert.Equal((ushort)2, decoded.ChannelId);
        Assert.Equal((ulong)1234, decoded.SenderEntityId);
        Assert.Equal("server", decoded.SenderName);
        Assert.Equal("Maintenance in 10 minutes.", decoded.Text);
        Assert.Equal((uint)77, decoded.ServerTick);
        Assert.Equal(ChatMessageFlags.System | ChatMessageFlags.Announcement, decoded.Flags);
    }

    private static void UiPanelOpenEventPacket_RoundTripsDocumentBytes()
    {
        byte[] documentBytes = [1, 2, 3, 4, 5];
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(new UiPanelOpenEventPacket(documentBytes));

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(UiPanelOpenEventPacket.PacketId, header.PacketId);
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        Assert.True(metadata.TryGet(UiPanelOpenEventPacket.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel));
        Assert.Equal(NetworkPlane.State, plane);
        Assert.Equal(PacketClass.ReliableEvent, packetClass);
        Assert.Equal(ChannelType.ReliableOrdered, channel);

        var reader = new BitReader(payload);
        UiPanelOpenEventPacket decoded = UiPanelOpenEventPacket.Deserialize(ref reader);
        Assert.True(documentBytes.AsSpan().SequenceEqual(decoded.DocumentBytes));
    }

    private static void UiActionRequestPacket_RoundTripsDocumentBytes()
    {
        byte[] documentBytes = [9, 8, 7, 6];
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(new UiActionRequestPacket(documentBytes));

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(UiActionRequestPacket.PacketId, header.PacketId);
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        Assert.True(metadata.TryGet(UiActionRequestPacket.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel));
        Assert.Equal(NetworkPlane.Input, plane);
        Assert.Equal(PacketClass.ReliableEvent, packetClass);
        Assert.Equal(ChannelType.ReliableOrdered, channel);

        var reader = new BitReader(payload);
        UiActionRequestPacket decoded = UiActionRequestPacket.Deserialize(ref reader);
        Assert.True(documentBytes.AsSpan().SequenceEqual(decoded.DocumentBytes));
    }

    private static void UiModelUpdateEventPacket_RoundTripsDocumentBytes()
    {
        byte[] documentBytes = [5, 4, 3, 2, 1];
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(new UiModelUpdateEventPacket(documentBytes));

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(UiModelUpdateEventPacket.PacketId, header.PacketId);
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        Assert.True(metadata.TryGet(UiModelUpdateEventPacket.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel));
        Assert.Equal(NetworkPlane.State, plane);
        Assert.Equal(PacketClass.ReliableEvent, packetClass);
        Assert.Equal(ChannelType.ReliableOrdered, channel);

        var reader = new BitReader(payload);
        UiModelUpdateEventPacket decoded = UiModelUpdateEventPacket.Deserialize(ref reader);
        Assert.True(documentBytes.AsSpan().SequenceEqual(decoded.DocumentBytes));
    }

    private static void UiDocumentChunkEventPacket_RoundTripsChunkMetadataAndBytes()
    {
        byte[] chunkBytes = [1, 3, 5, 7, 9];
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(new UiDocumentChunkEventPacket(
            UiDocumentChunkKind.PanelOpen,
            "open:panel/login/1",
            ChunkIndex: 2,
            ChunkCount: 4,
            chunkBytes));

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(UiDocumentChunkEventPacket.PacketId, header.PacketId);
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        Assert.True(metadata.TryGet(UiDocumentChunkEventPacket.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel));
        Assert.Equal(NetworkPlane.State, plane);
        Assert.Equal(PacketClass.ReliableEvent, packetClass);
        Assert.Equal(ChannelType.ReliableOrdered, channel);

        var reader = new BitReader(payload);
        UiDocumentChunkEventPacket decoded = UiDocumentChunkEventPacket.Deserialize(ref reader);
        Assert.Equal(UiDocumentChunkKind.PanelOpen, decoded.Kind);
        Assert.Equal("open:panel/login/1", decoded.TransferId);
        Assert.Equal((ushort)2, decoded.ChunkIndex);
        Assert.Equal((ushort)4, decoded.ChunkCount);
        Assert.True(chunkBytes.AsSpan().SequenceEqual(decoded.ChunkBytes));
    }

    private static void LoginCredentialSubmitPacket_RoundTripsTrustedCredentialMaterial()
    {
        byte[] material = [11, 22, 33, 44];
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(new LoginCredentialSubmitPacket(
            "admission/login/1/1",
            "login.submit",
            "Alice",
            LoginCredentialMaterialKind.PortalSessionToken,
            material));

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(LoginCredentialSubmitPacket.PacketId, header.PacketId);
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        Assert.True(metadata.TryGet(LoginCredentialSubmitPacket.PacketId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel));
        Assert.Equal(NetworkPlane.Input, plane);
        Assert.Equal(PacketClass.ReliableEvent, packetClass);
        Assert.Equal(ChannelType.ReliableOrdered, channel);

        var reader = new BitReader(payload);
        LoginCredentialSubmitPacket decoded = LoginCredentialSubmitPacket.Deserialize(ref reader);
        Assert.Equal("admission/login/1/1", decoded.PanelInstanceId);
        Assert.Equal("login.submit", decoded.ActionId);
        Assert.Equal("Alice", decoded.Username);
        Assert.Equal(LoginCredentialMaterialKind.PortalSessionToken, decoded.MaterialKind);
        Assert.True(material.AsSpan().SequenceEqual(decoded.MaterialBytes));
    }

    private static void RealtimeStateBatchPacket_RoundTripsExpectedItems()
    {
        RealtimeStateBatchItem[] items =
        [
            new(10, 1001, ImmutablePayloadBuffer.CopyFrom([1, 2])),
            new(11, 1002, ImmutablePayloadBuffer.CopyFrom([3, 4, 5]))
        ];

        ImmutablePayloadBuffer encoded = PacketCodec.Encode(new RealtimeStateBatchPacket(items));

        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(RealtimeStateBatchPacket.PacketId, header.PacketId);

        var reader = new BitReader(payload);
        RealtimeStateBatchPacket decoded = RealtimeStateBatchPacket.Deserialize(ref reader);

        Assert.Equal(2, decoded.Items.Length);
        Assert.Equal((ulong)10, decoded.Items[0].Sequence);
        Assert.Equal((ulong)1001, decoded.Items[0].ItemKey);
        Assert.True(decoded.Items[0].Payload.Span.SequenceEqual(new byte[] { 1, 2 }));
        Assert.Equal((ulong)11, decoded.Items[1].Sequence);
        Assert.Equal((ulong)1002, decoded.Items[1].ItemKey);
        Assert.True(decoded.Items[1].Payload.Span.SequenceEqual(new byte[] { 3, 4, 5 }));
    }

    private static void PacketRegistry_DispatchesTypedPacketAndMetadata()
    {
        var registry = new PacketRegistry<List<string>>();
        registry.Register<HandshakeHelloPacket>(static (List<string> messages, in HandshakeHelloPacket packet) =>
        {
            messages.Add($"hello:{packet.ProtocolVersion}:{packet.Features}");
        });
        registry.Seal();

        var hello = new HandshakeHelloPacket(3, 4, 5, FeatureFlags.BulkTransfer);
        ImmutablePayloadBuffer encoded = PacketCodec.Encode(hello);
        Assert.True(PacketCodec.TryDecodeEnvelope(encoded.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));

        var messages = new List<string>();
        Assert.True(registry.TryDispatch(messages, header, payload));
        Assert.Equal(ChannelType.ReliableOrdered, registry.GetChannel(HandshakeHelloPacket.PacketId));
        Assert.Equal(NetworkPlane.Control, registry.GetPlane(HandshakeHelloPacket.PacketId));
        Assert.Equal(PacketClass.Control, registry.GetPacketClass(HandshakeHelloPacket.PacketId));
        Assert.Equal("hello:3:BulkTransfer", messages[0]);
    }

    private static void PacketMetadataRegistry_ResolvesRegisteredPacketMetadata()
    {
        PacketMetadataRegistry registry = DefaultPacketMetadata.Create();

        Assert.True(registry.TryGet(HandshakeHelloPacket.PacketId, out NetworkPlane helloPlane, out PacketClass helloClass, out ChannelType helloChannel));
        Assert.Equal(NetworkPlane.Control, helloPlane);
        Assert.Equal(PacketClass.Control, helloClass);
        Assert.Equal(ChannelType.ReliableOrdered, helloChannel);

        Assert.True(registry.TryGet(MovementInputPacket.PacketId, out NetworkPlane inputPlane, out PacketClass inputClass, out ChannelType inputChannel));
        Assert.Equal(NetworkPlane.Input, inputPlane);
        Assert.Equal(PacketClass.Realtime, inputClass);
        Assert.Equal(ChannelType.UnreliableSequenced, inputChannel);

        Assert.False(registry.TryGet(0x7FFF, out _, out _, out _));
    }

    private static void HandshakeGate_AcceptsAndRejectsExpectedReasons()
    {
        var policy = new HandshakePolicy(
            ProtocolVersion: 8,
            SchemaHash: 100,
            ContentHash: 200,
            RequiredFeatures: FeatureFlags.SharedPayloadFanout);

        var acceptedHello = new HandshakeHelloPacket(8, 100, 200, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression);
        HandshakeValidationResult accepted = HandshakeGate.Validate(acceptedHello, policy);
        Assert.True(accepted.Accepted);

        var rejectedHello = new HandshakeHelloPacket(8, 100, 201, FeatureFlags.None);
        HandshakeValidationResult rejected = HandshakeGate.Validate(rejectedHello, policy);
        Assert.False(rejected.Accepted);
        Assert.Equal(HandshakeRejectReason.ContentHashMismatch, rejected.RejectReason);
    }

    private static void NetworkSessionManager_CreatesTransitionsAndDrainsLifecycleEvents()
    {
        var sessions = new NetworkSessionManager();
        SessionId sessionId = sessions.CreateSession(new ConnectionId(5), nowTicks: 10);
        Assert.True(sessions.TryMarkHandshaking(sessionId, 11));
        Assert.True(sessions.TryMarkAuthenticated(sessionId, 12));
        Assert.True(sessions.TryMarkReady(sessionId, 13));
        Assert.True(sessions.TryBeginDisconnect(sessionId, SessionDisconnectReason.Closed, 14));
        Assert.True(sessions.TryCompleteDisconnect(sessionId, 15));

        Assert.True(sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, snapshot.State);
        Assert.Equal(SessionDisconnectReason.Closed, snapshot.DisconnectReason);

        SessionLifecycleEvent[] events = new SessionLifecycleEvent[8];
        int count = sessions.DrainLifecycleEvents(events);
        Assert.Equal(6, count);
        Assert.Equal(SessionLifecycleEventKind.Created, events[0].Kind);
        Assert.Equal(ConnectionSessionState.Connecting, events[0].CurrentState);
        Assert.Equal(ConnectionSessionState.Handshaking, events[1].CurrentState);
        Assert.Equal(ConnectionSessionState.Authenticated, events[2].CurrentState);
        Assert.Equal(ConnectionSessionState.Ready, events[3].CurrentState);
        Assert.Equal(ConnectionSessionState.Disconnecting, events[4].CurrentState);
        Assert.Equal(ConnectionSessionState.Disconnected, events[5].CurrentState);
    }

    private static void NetworkSessionManager_AdmissionPolicy_RejectsAndReplacesDuplicates()
    {
        var rejecting = new NetworkSessionManager(new SessionAdmissionPolicy(
            DuplicateSessionPolicy.RejectNew,
            MaxActiveSessions: 0,
            AuthTimeoutTicks: 0,
            KeepAliveTimeoutTicks: 0));
        SessionId first = rejecting.CreateSession(new ConnectionId(7), 1);
        Assert.False(rejecting.TryCreateSession(new ConnectionId(7), 2, out _, out SessionDisconnectReason duplicateReason));
        Assert.Equal(SessionDisconnectReason.DuplicateSession, duplicateReason);
        Assert.True(rejecting.TryGetSnapshot(first, out SessionSnapshot rejectSnapshot));
        Assert.Equal(ConnectionSessionState.Connecting, rejectSnapshot.State);

        var replacing = new NetworkSessionManager(new SessionAdmissionPolicy(
            DuplicateSessionPolicy.ReplaceExisting,
            MaxActiveSessions: 0,
            AuthTimeoutTicks: 0,
            KeepAliveTimeoutTicks: 0));
        SessionId replaceFirst = replacing.CreateSession(new ConnectionId(8), 1);
        Assert.True(replacing.TryCreateSession(new ConnectionId(8), 2, out SessionId replaceSecond, out SessionDisconnectReason replaceReason));
        Assert.Equal(SessionDisconnectReason.None, replaceReason);
        Assert.False(replacing.TryGetSnapshot(replaceFirst, out _));
        Assert.True(replacing.TryGetSnapshot(replaceSecond, out SessionSnapshot replaceSnapshot));
        Assert.Equal(ConnectionSessionState.Connecting, replaceSnapshot.State);
    }

    private static void NetworkSessionManager_SweepTimeouts_DisconnectsExpiredSessions()
    {
        var sessions = new NetworkSessionManager(new SessionAdmissionPolicy(
            DuplicateSessionPolicy.RejectNew,
            MaxActiveSessions: 0,
            AuthTimeoutTicks: 5,
            KeepAliveTimeoutTicks: 5));
        SessionId authSession = sessions.CreateSession(new ConnectionId(10), 100);
        Assert.True(sessions.TryMarkHandshaking(authSession, 101));

        Assert.Equal(1, sessions.SweepTimeouts(106));
        Assert.True(sessions.TryGetSnapshot(authSession, out SessionSnapshot authSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, authSnapshot.State);
        Assert.Equal(SessionDisconnectReason.Timeout, authSnapshot.DisconnectReason);

        SessionId readySession = sessions.CreateSession(new ConnectionId(11), 200);
        Assert.True(sessions.TryMarkHandshaking(readySession, 201));
        Assert.True(sessions.TryMarkAuthenticated(readySession, 202));
        Assert.True(sessions.TryMarkReady(readySession, 203));
        Assert.True(sessions.TryTouchActivity(readySession, 204));

        Assert.Equal(1, sessions.SweepTimeouts(209));
        Assert.True(sessions.TryGetSnapshot(readySession, out SessionSnapshot readySnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, readySnapshot.State);
    }

    private static void NetworkSessionManager_MetricsSnapshot_TracksRejectsTimeoutsAndPeak()
    {
        var sessions = new NetworkSessionManager(new SessionAdmissionPolicy(
            DuplicateSessionPolicy.RejectNew,
            MaxActiveSessions: 1,
            AuthTimeoutTicks: 5,
            KeepAliveTimeoutTicks: 5));

        SessionId first = sessions.CreateSession(new ConnectionId(20), 100);
        Assert.False(sessions.TryCreateSession(new ConnectionId(20), 101, out _, out SessionDisconnectReason duplicateReason));
        Assert.Equal(SessionDisconnectReason.DuplicateSession, duplicateReason);
        Assert.True(sessions.TryMarkHandshaking(first, 102));
        Assert.Equal(1, sessions.SweepTimeouts(107));
        Assert.True(sessions.TryRemoveDisconnected(first, 108));

        SessionMetricsSnapshot metrics = sessions.GetMetricsSnapshot();
        Assert.Equal(0, metrics.TotalSessions);
        Assert.Equal(1, metrics.PeakConcurrentSessions);
        Assert.Equal(1, metrics.CreatedSessions);
        Assert.Equal(1, metrics.RejectedSessions);
        Assert.Equal(1, metrics.TimeoutDisconnects);
        Assert.Equal(1, metrics.RemovedSessions);
    }

    private static void ControlPlane_LoopbackHandshake_RoundTripsOverTransport()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, pair.Server, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, pair.Client, clientHello: hello);
        server.Start();
        client.Start();

        SessionId clientSession = client.BeginClientHandshake(new ConnectionId(1), nowTicks: 100);
        server.Pump(101);
        client.Pump(102);

        Assert.True(server.Sessions.TryGetSessionIdByConnection(new ConnectionId(1), out SessionId serverSession));
        Assert.True(server.Sessions.TryGetSnapshot(serverSession, out SessionSnapshot serverSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, serverSnapshot.State);

        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot clientSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, clientSnapshot.State);
    }

    private static void ControlPlane_UdpHandshake_RoundTripsOverTransport()
    {
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        using var serverTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata));
        serverTransport.Start();
        var serverEndPoint = (IPEndPoint)Assert.NotNull(serverTransport.LocalEndPoint);

        using var clientTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            serverEndPoint,
            new ConnectionId(1),
            AllowDynamicConnections: false));
        clientTransport.Start();

        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, serverTransport, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, clientTransport, clientHello: hello);

        SessionId clientSession = client.BeginClientHandshake(new ConnectionId(1), nowTicks: 100);
        PumpUntilHandshakeReady(server, client, serverTicksStart: 101, clientTicksStart: 102);

        Assert.True(server.Sessions.TryGetSessionIdByConnection(new ConnectionId(1), out SessionId serverSession));
        Assert.True(server.Sessions.TryGetSnapshot(serverSession, out SessionSnapshot serverSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, serverSnapshot.State);

        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot clientSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, clientSnapshot.State);
    }

    private static void ControlPlane_RetryClientHandshake_ReSendsHelloWithoutDuplicateLocalSession()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, pair.Server, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, pair.Client, clientHello: hello);
        server.Start();
        client.Start();

        SessionId clientSession = client.BeginClientHandshake(new ConnectionId(1), nowTicks: 100);
        var dropped = new InboundDatagram[1];
        int droppedCount = pair.Server.PollInbound(dropped);
        Assert.Equal(1, droppedCount);
        dropped[0].Dispose();

        Assert.True(client.RetryClientHandshake(new ConnectionId(1), nowTicks: 101));
        PumpUntilHandshakeReady(server, client, serverTicksStart: 102, clientTicksStart: 103);

        Assert.Equal(1, client.Sessions.Count);
        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot clientSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, clientSnapshot.State);
        Assert.True(server.Sessions.TryGetSessionIdByConnection(new ConnectionId(1), out SessionId serverSession));
        Assert.True(server.Sessions.TryGetSnapshot(serverSession, out SessionSnapshot serverSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, serverSnapshot.State);
    }

    private static void ControlPlane_LoopbackHandshake_RejectsInvalidHello()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var badHello = new HandshakeHelloPacket(9, 10, 99, FeatureFlags.None);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, pair.Server, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, pair.Client, clientHello: badHello);
        server.Start();
        client.Start();

        SessionId clientSession = client.BeginClientHandshake(new ConnectionId(2), nowTicks: 200);
        server.Pump(201);
        client.Pump(202);

        Assert.True(server.Sessions.TryGetSessionIdByConnection(new ConnectionId(2), out SessionId serverSession));
        Assert.True(server.Sessions.TryGetSnapshot(serverSession, out SessionSnapshot serverSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, serverSnapshot.State);
        Assert.Equal(SessionDisconnectReason.Rejected, serverSnapshot.DisconnectReason);

        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot clientSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, clientSnapshot.State);
        Assert.Equal(SessionDisconnectReason.Rejected, clientSnapshot.DisconnectReason);

        ControlPlaneEvent[] serverEvents = new ControlPlaneEvent[4];
        int serverEventCount = server.DrainEvents(serverEvents);
        Assert.Equal(1, serverEventCount);
        Assert.Equal(ControlPlaneEventKind.HandshakeRejected, serverEvents[0].Kind);
        Assert.Equal(HandshakeRejectReason.ContentHashMismatch, serverEvents[0].HandshakeRejectReason);
    }

    private static void ControlPlane_AdmissionReject_ReportsReasonAndMetrics()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var sessionPolicy = new SessionAdmissionPolicy(
            DuplicateSessionPolicy.RejectNew,
            MaxActiveSessions: 1,
            AuthTimeoutTicks: 0,
            KeepAliveTimeoutTicks: 0);
        var server = new NetworkControlPlaneHost(
            NetworkControlPlaneRole.Server,
            pair.Server,
            handshakePolicy: policy,
            sessionPolicy: sessionPolicy);
        var client = new NetworkControlPlaneHost(
            NetworkControlPlaneRole.Client,
            pair.Client,
            clientHello: hello);
        server.Start();
        client.Start();

        client.BeginClientHandshake(new ConnectionId(30), 300);
        server.Pump(301);
        client.Pump(302);

        SessionId rejectedClientSession = client.BeginClientHandshake(new ConnectionId(31), 303);
        server.Pump(304);
        client.Pump(305);

        Assert.True(client.Sessions.TryGetSnapshot(rejectedClientSession, out SessionSnapshot rejectedSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, rejectedSnapshot.State);
        Assert.Equal(SessionDisconnectReason.Rejected, rejectedSnapshot.DisconnectReason);

        ControlPlaneMetricsSnapshot serverMetrics = server.GetMetricsSnapshot();
        Assert.Equal(1, serverMetrics.HandshakeAccepts);
        Assert.Equal(1, serverMetrics.HandshakeRejects);
        Assert.Equal(1, serverMetrics.AdmissionRejects);

        ControlPlaneEvent[] events = new ControlPlaneEvent[4];
        int eventCount = server.DrainEvents(events);
        Assert.Equal(2, eventCount);
        Assert.Equal(ControlPlaneEventKind.HandshakeAccepted, events[0].Kind);
        Assert.Equal(ControlPlaneEventKind.SessionAdmissionRejected, events[1].Kind);
        Assert.Equal(HandshakeRejectReason.ServerBusy, events[1].HandshakeRejectReason);
        Assert.Equal(SessionDisconnectReason.ServerBusy, events[1].DisconnectReason);
    }

    private static void ControlPlane_PingProbe_RoundTripsAfterReadyHandshake()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, pair.Server, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(
            NetworkControlPlaneRole.Client,
            pair.Client,
            clientHello: hello,
            sessionPolicy: new SessionAdmissionPolicy(
                DuplicateSessionPolicy.RejectNew,
                MaxActiveSessions: 0,
                AuthTimeoutTicks: 0,
                KeepAliveTimeoutTicks: 10));
        server.Start();
        client.Start();

        SessionId clientSession = client.BeginClientHandshake(new ConnectionId(3), nowTicks: 300);
        server.Pump(301);
        client.Pump(302);
        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot snapshot));
        Assert.Equal(ConnectionSessionState.Ready, snapshot.State);

        Assert.True(client.SendPingProbe(new ConnectionId(3), sequence: 17, clientTick: 303));
        server.Pump(304);
        client.Pump(305);

        Assert.Equal(1, client.ReceivedPongs.Count);
        Assert.Equal((ushort)17, client.ReceivedPongs[0].Sequence);
        Assert.Equal((uint)303, client.ReceivedPongs[0].ClientTick);
        Assert.Equal((uint)304, client.ReceivedPongs[0].ServerTick);

        ControlPlaneMetricsSnapshot serverMetrics = server.GetMetricsSnapshot();
        ControlPlaneMetricsSnapshot clientMetrics = client.GetMetricsSnapshot();
        Assert.Equal(1, serverMetrics.PingProbesReceived);
        Assert.Equal(1, serverMetrics.PongProbesSent);
        Assert.Equal(1, clientMetrics.PingProbesSent);
        Assert.Equal(1, clientMetrics.PongProbesReceived);
    }

    private static void ControlPlane_ServerDisconnectNotice_DisconnectsClientSession()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, pair.Server, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, pair.Client, clientHello: hello);
        server.Start();
        client.Start();

        SessionId clientSession = client.BeginClientHandshake(new ConnectionId(3), nowTicks: 300);
        server.Pump(301);
        client.Pump(302);
        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot readySnapshot));
        Assert.Equal(ConnectionSessionState.Ready, readySnapshot.State);

        Assert.True(server.SendDisconnectNotice(new ConnectionId(3), SessionDisconnectReason.ServerShutdown, 303));
        server.FlushOutbound();
        client.Pump(304);

        Assert.True(client.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot disconnectedSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, disconnectedSnapshot.State);
        Assert.Equal(SessionDisconnectReason.ServerShutdown, disconnectedSnapshot.DisconnectReason);

        ControlPlaneEvent[] events = new ControlPlaneEvent[4];
        int eventCount = client.DrainEvents(events);
        Assert.True(eventCount >= 1);
        Assert.Equal(ControlPlaneEventKind.ServerDisconnected, events[eventCount - 1].Kind);
        Assert.Equal(SessionDisconnectReason.ServerShutdown, events[eventCount - 1].DisconnectReason);
    }

    private static void ControlPlane_ClientDisconnectNotice_DisconnectsServerSession()
    {
        using var pair = new LoopbackTransportPair();
        var policy = new HandshakePolicy(
            ProtocolVersion: 9,
            SchemaHash: 10,
            ContentHash: 11,
            RequiredFeatures: FeatureFlags.Compression);
        var hello = new HandshakeHelloPacket(9, 10, 11, FeatureFlags.Compression);
        var server = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, pair.Server, handshakePolicy: policy);
        var client = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, pair.Client, clientHello: hello);
        server.Start();
        client.Start();

        client.BeginClientHandshake(new ConnectionId(3), nowTicks: 300);
        server.Pump(301);
        client.Pump(302);
        Assert.True(server.Sessions.TryGetSessionIdByConnection(new ConnectionId(3), out SessionId serverSession));
        Assert.True(server.Sessions.TryGetSnapshot(serverSession, out SessionSnapshot readySnapshot));
        Assert.Equal(ConnectionSessionState.Ready, readySnapshot.State);

        Assert.True(client.SendClientDisconnectNotice(new ConnectionId(3), SessionDisconnectReason.Closed, 303));
        server.Pump(304);

        Assert.True(server.Sessions.TryGetSnapshot(serverSession, out SessionSnapshot disconnectedSnapshot));
        Assert.Equal(ConnectionSessionState.Disconnected, disconnectedSnapshot.State);
        Assert.Equal(SessionDisconnectReason.Closed, disconnectedSnapshot.DisconnectReason);

        ControlPlaneEvent[] events = new ControlPlaneEvent[4];
        int eventCount = server.DrainEvents(events);
        Assert.True(eventCount >= 1);
        Assert.Equal(ControlPlaneEventKind.ClientDisconnected, events[eventCount - 1].Kind);
        Assert.Equal(SessionDisconnectReason.Closed, events[eventCount - 1].DisconnectReason);
    }

    private static void NetworkSessionManager_SessionIdExhaustion_RejectsGracefully()
    {
        var sessions = new NetworkSessionManager();
        FieldInfo? field = typeof(NetworkSessionManager).GetField("_nextSessionId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(sessions, 0u);

        bool created = sessions.TryCreateSession(new ConnectionId(77), 100, out SessionId sessionId, out SessionDisconnectReason rejectReason);

        Assert.False(created);
        Assert.Equal(SessionId.Invalid, sessionId);
        Assert.Equal(SessionDisconnectReason.ServerBusy, rejectReason);
    }

    private static void LatestStateStream_CoalescesAndDrainsByLatestVersion()
    {
        var stream = new LatestStateStream();
        SubscriptionKey keyA = new(11);
        SubscriptionKey keyB = new(22);
        stream.Publish(1, keyA, ImmutablePayloadBuffer.CopyFrom([1]));
        ulong newerSequence = stream.Publish(1, keyA, ImmutablePayloadBuffer.CopyFrom([2]));
        ulong otherSequence = stream.Publish(2, keyB, ImmutablePayloadBuffer.CopyFrom([3]));

        LatestStateSnapshot snapshot = stream.GetSnapshot();
        Assert.Equal(2, snapshot.ItemCount);
        Assert.Equal(otherSequence, snapshot.HighestPublishedSequence);

        var updates = new LatestStateUpdate[4];
        LatestStateDrainResult result = stream.DrainSince(default, updates);

        Assert.Equal(2, result.Count);
        Assert.Equal(newerSequence, updates[0].Sequence);
        Assert.Equal((ulong)11, updates[0].ScopeKey.Value);
        Assert.Equal(PacketClass.Realtime, updates[0].PacketClass);
        Assert.Equal((byte)2, updates[0].Payload.Span[0]);
        Assert.Equal((ulong)2, updates[1].ItemKey);
        Assert.Equal((ulong)22, updates[1].ScopeKey.Value);
        Assert.Equal(PacketClass.Realtime, updates[1].PacketClass);
        Assert.Equal((byte)3, updates[1].Payload.Span[0]);
        Assert.Equal(otherSequence, result.NextCursor.LastSeenSequence);
    }

    private static void LatestStateStream_PartialDrain_AdvancesCursor()
    {
        var stream = new LatestStateStream();
        ulong first = stream.Publish(10, new SubscriptionKey(10), ImmutablePayloadBuffer.CopyFrom([10]));
        ulong second = stream.Publish(20, new SubscriptionKey(20), ImmutablePayloadBuffer.CopyFrom([20]));
        ulong third = stream.Publish(30, new SubscriptionKey(30), ImmutablePayloadBuffer.CopyFrom([30]));

        var firstBatch = new LatestStateUpdate[2];
        LatestStateDrainResult firstResult = stream.DrainSince(default, firstBatch);
        Assert.Equal(2, firstResult.Count);
        Assert.Equal(first, firstBatch[0].Sequence);
        Assert.Equal(second, firstBatch[1].Sequence);

        var secondBatch = new LatestStateUpdate[2];
        LatestStateDrainResult secondResult = stream.DrainSince(firstResult.NextCursor, secondBatch);
        Assert.Equal(1, secondResult.Count);
        Assert.Equal(third, secondBatch[0].Sequence);
    }

    private static void LatestStateStream_PublishScopedCopies_PreservesSharedItemKeyAcrossScopes()
    {
        var stream = new LatestStateStream();
        SubscriptionKey[] scopes = [new SubscriptionKey(41), new SubscriptionKey(42)];

        ulong sequence = stream.PublishScopedCopies(
            77,
            scopes,
            PacketClass.Realtime,
            ImmutablePayloadBuffer.CopyFrom([9]));

        LatestStateSnapshot snapshot = stream.GetSnapshot();
        Assert.Equal(2, snapshot.ItemCount);

        var batch = new LatestStateUpdate[4];
        LatestStateDrainResult drain = stream.DrainSince(default, batch);
        Assert.Equal(2, drain.Count);
        Assert.Equal(sequence, batch[0].Sequence);
        Assert.Equal(sequence, batch[1].Sequence);
        Assert.Equal((ulong)77, batch[0].ItemKey);
        Assert.Equal((ulong)77, batch[1].ItemKey);
        Assert.True(batch[0].ScopeKey != batch[1].ScopeKey);
    }

    private static void OrderedEventStream_DrainsInOrderAndAdvancesCursor()
    {
        var stream = new OrderedEventStream(capacity: 4);
        ulong first = stream.Publish(new SubscriptionKey(41), ImmutablePayloadBuffer.CopyFrom([1]));
        ulong second = stream.Publish(new SubscriptionKey(42), ImmutablePayloadBuffer.CopyFrom([2]));
        ulong third = stream.Publish(new SubscriptionKey(43), ImmutablePayloadBuffer.CopyFrom([3]));

        OrderedEventSnapshot snapshot = stream.GetSnapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(first, snapshot.OldestAvailableSequence);
        Assert.Equal(third, snapshot.NewestAvailableSequence);

        var batch = new OrderedEventEntry[2];
        OrderedEventDrainResult firstDrain = stream.DrainSince(default, batch);
        Assert.Equal(OrderedEventDrainStatus.Success, firstDrain.Status);
        Assert.Equal(2, firstDrain.Count);
        Assert.Equal(first, batch[0].Sequence);
        Assert.Equal((ulong)41, batch[0].ScopeKey.Value);
        Assert.Equal(PacketClass.ReliableEvent, batch[0].PacketClass);
        Assert.Equal(second, batch[1].Sequence);
        Assert.Equal((ulong)42, batch[1].ScopeKey.Value);
        Assert.Equal(PacketClass.ReliableEvent, batch[1].PacketClass);

        var nextBatch = new OrderedEventEntry[2];
        OrderedEventDrainResult secondDrain = stream.DrainSince(firstDrain.NextCursor, nextBatch);
        Assert.Equal(1, secondDrain.Count);
        Assert.Equal(third, nextBatch[0].Sequence);
    }

    private static void LatestStateStream_CopySnapshot_ReturnsAllUpdatesSorted()
    {
        var stream = new LatestStateStream();
        ulong first = stream.Publish(100, new SubscriptionKey(1), ImmutablePayloadBuffer.CopyFrom([1]));
        ulong second = stream.Publish(200, new SubscriptionKey(2), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([2]));

        var batch = new LatestStateUpdate[4];
        int count = stream.CopySnapshot(batch);

        Assert.Equal(2, count);
        Assert.Equal(first, batch[0].Sequence);
        Assert.Equal((ulong)100, batch[0].ItemKey);
        Assert.Equal(second, batch[1].Sequence);
        Assert.Equal(PacketClass.Bulk, batch[1].PacketClass);
    }

    private static void LatestStateStream_CopySnapshotForScopes_FiltersAndPreservesSequenceOrder()
    {
        var stream = new LatestStateStream();
        SubscriptionKey scopeA = new(11);
        SubscriptionKey scopeB = new(22);
        SubscriptionKey scopeC = new(33);

        ulong first = stream.Publish(100, scopeA, ImmutablePayloadBuffer.CopyFrom([1]));
        ulong second = stream.Publish(200, scopeB, ImmutablePayloadBuffer.CopyFrom([2]));
        ulong third = stream.Publish(300, scopeA, PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([3]));
        ulong fourth = stream.Publish(400, scopeC, ImmutablePayloadBuffer.CopyFrom([4]));

        SubscriptionKey[] scopes = [scopeA, scopeC];
        var batch = new LatestStateUpdate[8];

        int count = stream.CopySnapshotForScopes(scopes, batch);

        Assert.Equal(3, count);
        Assert.Equal(first, batch[0].Sequence);
        Assert.Equal(scopeA, batch[0].ScopeKey);
        Assert.Equal(third, batch[1].Sequence);
        Assert.Equal(scopeA, batch[1].ScopeKey);
        Assert.Equal(PacketClass.Bulk, batch[1].PacketClass);
        Assert.Equal(fourth, batch[2].Sequence);
        Assert.Equal(scopeC, batch[2].ScopeKey);
        Assert.False(batch[..count].Contains(new LatestStateUpdate(200, second, scopeB, PacketClass.Realtime, ImmutablePayloadBuffer.CopyFrom([2]))));
    }

    private static void ConnectionLatestStateSentTracker_TracksStableItemIdentityPerConnection()
    {
        var tracker = new ConnectionLatestStateSentTracker();
        ConnectionId connection1 = new(31);
        ConnectionId connection2 = new(32);
        LatestStateUpdate first = new(
            ItemKey: 1001,
            Sequence: 10,
            ScopeKey: new SubscriptionKey(11),
            PacketClass: PacketClass.Realtime,
            Payload: ImmutablePayloadBuffer.CopyFrom([1]));
        LatestStateUpdate movedScope = new(
            ItemKey: 1001,
            Sequence: 12,
            ScopeKey: new SubscriptionKey(22),
            PacketClass: PacketClass.Realtime,
            Payload: ImmutablePayloadBuffer.CopyFrom([2]));

        tracker.MarkSent(connection1, first);
        tracker.MarkSent(connection1, movedScope);

        Assert.True(tracker.TryGetLastSentSequence(connection1, 1001, out ulong connection1Sequence));
        Assert.Equal((ulong)12, connection1Sequence);
        Assert.False(tracker.TryGetLastSentSequence(connection2, 1001, out _));
    }

    private static void ConnectionLatestStateDeltaPlanner_FiltersSnapshotAgainstLastSentPerConnection()
    {
        var tracker = new ConnectionLatestStateSentTracker();
        var planner = new ConnectionLatestStateDeltaPlanner(tracker);
        ConnectionId connectionId = new(33);
        LatestStateUpdate[] snapshot =
        [
            new(
                ItemKey: 1001,
                Sequence: 10,
                ScopeKey: new SubscriptionKey(11),
                PacketClass: PacketClass.Realtime,
                Payload: ImmutablePayloadBuffer.CopyFrom([1])),
            new(
                ItemKey: 1002,
                Sequence: 11,
                ScopeKey: new SubscriptionKey(12),
                PacketClass: PacketClass.Realtime,
                Payload: ImmutablePayloadBuffer.CopyFrom([2])),
            new(
                ItemKey: 1001,
                Sequence: 12,
                ScopeKey: new SubscriptionKey(22),
                PacketClass: PacketClass.Realtime,
                Payload: ImmutablePayloadBuffer.CopyFrom([3])),
        ];

        tracker.MarkSent(connectionId, snapshot[0]);
        var output = new LatestStateUpdate[4];

        ConnectionLatestStateDeltaPlanResult result = planner.Plan(connectionId, snapshot, output);

        Assert.Equal(2, result.Count);
        Assert.Equal((ulong)12, result.HighestPlannedSequence);
        Assert.Equal((ulong)1002, output[0].ItemKey);
        Assert.Equal((ulong)11, output[0].Sequence);
        Assert.Equal((ulong)1001, output[1].ItemKey);
        Assert.Equal((ulong)12, output[1].Sequence);
        Assert.Equal((ulong)22, output[1].ScopeKey.Value);
    }

    private static void StreamPrimitives_PreserveBulkPacketClassMetadata()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 4);

        ulong latestSequence = latest.Publish(7, new SubscriptionKey(71), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([7]));
        ulong eventSequence = ordered.Publish(new SubscriptionKey(81), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([8]));

        var latestBatch = new LatestStateUpdate[2];
        LatestStateDrainResult latestDrain = latest.DrainSince(default, latestBatch);
        Assert.Equal(1, latestDrain.Count);
        Assert.Equal(latestSequence, latestBatch[0].Sequence);
        Assert.Equal(PacketClass.Bulk, latestBatch[0].PacketClass);

        var eventBatch = new OrderedEventEntry[2];
        OrderedEventDrainResult eventDrain = ordered.DrainSince(default, eventBatch);
        Assert.Equal(1, eventDrain.Count);
        Assert.Equal(eventSequence, eventBatch[0].Sequence);
        Assert.Equal(PacketClass.Bulk, eventBatch[0].PacketClass);
    }

    private static void OrderedEventStream_RequiresRecoveryWhenCursorFallsBehind()
    {
        var stream = new OrderedEventStream(capacity: 2);
        ulong first = stream.Publish(new SubscriptionKey(51), ImmutablePayloadBuffer.CopyFrom([1]));
        stream.Publish(new SubscriptionKey(52), ImmutablePayloadBuffer.CopyFrom([2]));
        ulong third = stream.Publish(new SubscriptionKey(53), ImmutablePayloadBuffer.CopyFrom([3]));

        var batch = new OrderedEventEntry[2];
        OrderedEventDrainResult drain = stream.DrainSince(new OrderedEventCursor(first), batch);
        Assert.Equal(OrderedEventDrainStatus.RecoveryRequired, drain.Status);
        Assert.Equal(0, drain.Count);
        Assert.Equal(third - 1, drain.NextCursor.NextSequence);
        Assert.Equal(third - 1, drain.OldestAvailableSequence);
        Assert.Equal(third, drain.NewestAvailableSequence);
    }

    private static void ReliableEventReplayBuffer_TracksAcknowledgesAndReplaysInOrder()
    {
        var buffer = new ReliableEventReplayBuffer(capacityPerConnection: 4);
        ConnectionId connectionId = new(7);

        buffer.Track(connectionId, sentTick: 100, sequence: 10, new SubscriptionKey(1), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([1]));
        buffer.Track(connectionId, sentTick: 101, sequence: 11, new SubscriptionKey(2), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([2]));
        buffer.Track(connectionId, sentTick: 102, sequence: 12, new SubscriptionKey(3), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([3]));

        Assert.True(buffer.TryGetOldestSequence(connectionId, out ulong oldest));
        Assert.Equal((ulong)10, oldest);
        Assert.True(buffer.TryGetNewestSequence(connectionId, out ulong newest));
        Assert.Equal((ulong)12, newest);

        Assert.Equal(1, buffer.AcknowledgeUpTo(connectionId, 10));

        var replay = new ReliableEventReplayEntry[4];
        int copied = buffer.CopyPendingSince(connectionId, 11, replay, out bool recoveryRequired);

        Assert.False(recoveryRequired);
        Assert.Equal(2, copied);
        Assert.Equal((ulong)11, replay[0].Sequence);
        Assert.Equal((ulong)12, replay[1].Sequence);
        Assert.Equal(PacketClass.Bulk, replay[1].PacketClass);
    }

    private static void ReliableEventReplayBuffer_RequiresRecoveryWhenRequestedSequenceFallsBehindWindow()
    {
        var buffer = new ReliableEventReplayBuffer(capacityPerConnection: 2);
        ConnectionId connectionId = new(8);

        buffer.Track(connectionId, sentTick: 100, sequence: 20, new SubscriptionKey(1), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([1]));
        buffer.Track(connectionId, sentTick: 101, sequence: 21, new SubscriptionKey(2), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([2]));
        buffer.Track(connectionId, sentTick: 102, sequence: 22, new SubscriptionKey(3), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([3]));

        var replay = new ReliableEventReplayEntry[4];
        int copied = buffer.CopyPendingSince(connectionId, 20, replay, out bool recoveryRequired);

        Assert.True(recoveryRequired);
        Assert.Equal(0, copied);
        Assert.True(buffer.TryGetOldestSequence(connectionId, out ulong oldest));
        Assert.Equal((ulong)21, oldest);
    }

    private static void ReliableEventRetransmitPlanner_WaitsForThresholdThenReturnsBoundedReplaySet()
    {
        var replay = new ReliableEventReplayBuffer(capacityPerConnection: 8);
        var acks = new ReliableEventAckTracker();
        ConnectionId connectionId = new(15);
        replay.Track(connectionId, sentTick: 100, sequence: 10, new SubscriptionKey(1), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([1]));
        replay.Track(connectionId, sentTick: 101, sequence: 11, new SubscriptionKey(2), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([2]));
        replay.Track(connectionId, sentTick: 102, sequence: 12, new SubscriptionKey(3), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([3]));
        acks.TryAcknowledge(connectionId, 9, out _);
        var planner = new ReliableEventRetransmitPlanner(replay, acks);
        var output = new ReliableEventReplayEntry[8];

        ReliableEventRetransmitPlanResult early = planner.Plan(
            connectionId,
            nowTick: 102,
            new ReliableEventRetransmitPolicy(RetransmitThresholdTicks: 3, MaxRetransmitItemsPerTick: 2),
            output);

        Assert.Equal(0, early.Count);
        Assert.False(early.RecoveryRequired);
        Assert.Equal((ulong)10, early.NextSequence);

        ReliableEventRetransmitPlanResult ready = planner.Plan(
            connectionId,
            nowTick: 103,
            new ReliableEventRetransmitPolicy(RetransmitThresholdTicks: 3, MaxRetransmitItemsPerTick: 2),
            output);

        Assert.Equal(2, ready.Count);
        Assert.False(ready.RecoveryRequired);
        Assert.Equal((ulong)10, output[0].Sequence);
        Assert.Equal((ulong)11, output[1].Sequence);
    }

    private static void ReliableEventRetransmitPlanner_RequestsRecoveryWhenAckFallsBehindReplayWindow()
    {
        var replay = new ReliableEventReplayBuffer(capacityPerConnection: 2);
        var acks = new ReliableEventAckTracker();
        ConnectionId connectionId = new(16);
        replay.Track(connectionId, sentTick: 100, sequence: 20, new SubscriptionKey(1), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([1]));
        replay.Track(connectionId, sentTick: 101, sequence: 21, new SubscriptionKey(2), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([2]));
        replay.Track(connectionId, sentTick: 102, sequence: 22, new SubscriptionKey(3), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([3]));
        acks.TryAcknowledge(connectionId, 19, out _);
        var planner = new ReliableEventRetransmitPlanner(replay, acks);
        var output = new ReliableEventReplayEntry[4];

        ReliableEventRetransmitPlanResult result = planner.Plan(
            connectionId,
            nowTick: 105,
            ReliableEventRetransmitPolicy.Default,
            output);

        Assert.Equal(0, result.Count);
        Assert.True(result.RecoveryRequired);
        Assert.Equal((ulong)20, result.NextSequence);
        Assert.Equal((ulong)21, result.OldestAvailableSequence);
    }

    private static void ReliableEventRetransmitBuilder_BuildsOrderedEventDatagrams()
    {
        var builder = new ReliableEventRetransmitBuilder();
        ReliableEventReplayEntry[] entries =
        [
            new(Sequence: 10, SentTick: 100, new SubscriptionKey(1), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([1, 2])),
            new(Sequence: 11, SentTick: 101, new SubscriptionKey(2), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([3, 4, 5])),
        ];
        var output = new OutboundDatagram[4];

        OutboundAssemblyResult result = builder.Build(new ConnectionId(17), entries, output);

        Assert.Equal(2, result.Count);
        Assert.Equal(PacketClass.ReliableEvent, output[0].PacketClass);
        Assert.Equal(PacketClass.Bulk, output[1].PacketClass);

        ReadOnlySpan<byte> payload0 = DecodePayload(output[0]);
        var reader0 = new BitReader(payload0);
        ReliableEventFramePacket frame0 = ReliableEventFramePacket.Deserialize(ref reader0);
        Assert.Equal((ulong)10, frame0.Sequence);
        Assert.True(frame0.Payload.Span.SequenceEqual(new byte[] { 1, 2 }));

        ReadOnlySpan<byte> payload1 = DecodePayload(output[1]);
        var reader1 = new BitReader(payload1);
        ReliableEventFramePacket frame1 = ReliableEventFramePacket.Deserialize(ref reader1);
        Assert.Equal((ulong)11, frame1.Sequence);
        Assert.True(frame1.Payload.Span.SequenceEqual(new byte[] { 3, 4, 5 }));
    }

    private static void ConnectionStreamScheduler_PrioritizesRealtimeThenReliableWithinBudget()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        SubscriptionKey latestKey = new(1001);
        SubscriptionKey eventKey = new(1002);
        ulong realtimeSequence = latest.Publish(100, latestKey, ImmutablePayloadBuffer.CopyFrom([1, 2]));
        ulong eventSequence = ordered.Publish(eventKey, ImmutablePayloadBuffer.CopyFrom([9]));
        var scheduler = new ConnectionStreamScheduler(latest, ordered);

        var work = new ScheduledWorkItem[4];
        ConnectionScheduleResult result = scheduler.Schedule(
            default,
            new ConnectionSendBudget(
                new PacketClassBudget(MaxItems: 2, MaxPayloadBytes: 8),
                new PacketClassBudget(MaxItems: 2, MaxPayloadBytes: 8),
                new PacketClassBudget(MaxItems: 2, MaxPayloadBytes: 8)),
            work);

        Assert.Equal(2, result.Count);
        Assert.False(result.OrderedEventRecoveryRequired);
        Assert.Equal(1, result.RealtimeItemsScheduled);
        Assert.Equal(1, result.ReliableEventItemsScheduled);
        Assert.Equal(ScheduledWorkKind.LatestState, work[0].Kind);
        Assert.Equal(PacketClass.Realtime, work[0].PacketClass);
        Assert.Equal(realtimeSequence, work[0].Sequence);
        Assert.Equal((ulong)100, work[0].ItemKey);
        Assert.Equal((ulong)1001, work[0].ScopeKey.Value);
        Assert.Equal(ScheduledWorkKind.OrderedEvent, work[1].Kind);
        Assert.Equal(PacketClass.ReliableEvent, work[1].PacketClass);
        Assert.Equal(eventSequence, work[1].Sequence);
        Assert.Equal((ulong)1002, work[1].ScopeKey.Value);
    }

    private static void ConnectionStreamScheduler_SchedulesBulkAgainstBulkBudget()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        ulong bulkLatest = latest.Publish(901, new SubscriptionKey(9001), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([1, 2, 3]));
        ulong bulkEvent = ordered.Publish(new SubscriptionKey(9002), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([4, 5]));
        var scheduler = new ConnectionStreamScheduler(latest, ordered);

        var work = new ScheduledWorkItem[4];
        ConnectionScheduleResult result = scheduler.Schedule(
            default,
            new ConnectionSendBudget(
                new PacketClassBudget(MaxItems: 0, MaxPayloadBytes: 0),
                new PacketClassBudget(MaxItems: 0, MaxPayloadBytes: 0),
                new PacketClassBudget(MaxItems: 2, MaxPayloadBytes: 8)),
            work);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result.RealtimeItemsScheduled);
        Assert.Equal(0, result.ReliableEventItemsScheduled);
        Assert.Equal(2, result.BulkItemsScheduled);
        Assert.Equal(PacketClass.Bulk, work[0].PacketClass);
        Assert.Equal(bulkLatest, work[0].Sequence);
        Assert.Equal(PacketClass.Bulk, work[1].PacketClass);
        Assert.Equal(bulkEvent, work[1].Sequence);
    }

    private static void ConnectionStreamScheduler_LeavesCursorsOnUnscheduledWork()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        ulong firstRealtime = latest.Publish(1, new SubscriptionKey(2001), ImmutablePayloadBuffer.CopyFrom([1]));
        ulong secondRealtime = latest.Publish(2, new SubscriptionKey(2002), ImmutablePayloadBuffer.CopyFrom([2]));
        ulong firstEvent = ordered.Publish(new SubscriptionKey(2003), ImmutablePayloadBuffer.CopyFrom([3]));
        var scheduler = new ConnectionStreamScheduler(latest, ordered);

        var work = new ScheduledWorkItem[4];
        ConnectionScheduleResult firstResult = scheduler.Schedule(
            default,
            new ConnectionSendBudget(
                new PacketClassBudget(MaxItems: 1, MaxPayloadBytes: 16),
                new PacketClassBudget(MaxItems: 0, MaxPayloadBytes: 0),
                new PacketClassBudget(MaxItems: 0, MaxPayloadBytes: 0)),
            work);

        Assert.Equal(1, firstResult.Count);
        Assert.Equal(firstRealtime, firstResult.NextState.LatestStateCursor.LastSeenSequence);
        Assert.Equal(0UL, firstResult.NextState.OrderedEventCursor.NextSequence);

        var nextWork = new ScheduledWorkItem[4];
        ConnectionScheduleResult secondResult = scheduler.Schedule(
            firstResult.NextState,
            new ConnectionSendBudget(
                new PacketClassBudget(MaxItems: 2, MaxPayloadBytes: 16),
                new PacketClassBudget(MaxItems: 1, MaxPayloadBytes: 16),
                new PacketClassBudget(MaxItems: 0, MaxPayloadBytes: 0)),
            nextWork);

        Assert.Equal(2, secondResult.Count);
        Assert.Equal(secondRealtime, nextWork[0].Sequence);
        Assert.Equal(firstEvent, nextWork[1].Sequence);
    }

    private static void ConnectionStreamScheduler_SignalsOrderedEventRecovery()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 2);
        latest.Publish(10, new SubscriptionKey(3001), ImmutablePayloadBuffer.CopyFrom([1]));
        ordered.Publish(new SubscriptionKey(3002), ImmutablePayloadBuffer.CopyFrom([2]));
        ordered.Publish(new SubscriptionKey(3003), ImmutablePayloadBuffer.CopyFrom([3]));
        ulong newestEvent = ordered.Publish(new SubscriptionKey(3004), ImmutablePayloadBuffer.CopyFrom([4]));
        var scheduler = new ConnectionStreamScheduler(latest, ordered);

        var work = new ScheduledWorkItem[4];
        ConnectionScheduleResult result = scheduler.Schedule(
            new ConnectionScheduleState(
                default,
                new OrderedEventCursor(1)),
            ConnectionSendBudget.Unlimited,
            work);

        Assert.True(result.OrderedEventRecoveryRequired);
        Assert.Equal(1, result.Count);
        Assert.Equal(ScheduledWorkKind.LatestState, work[0].Kind);
        Assert.Equal(newestEvent - 1, result.NextState.OrderedEventCursor.NextSequence);
        Assert.Equal(0, result.ReliableEventItemsScheduled);
        Assert.Equal(0, result.BulkItemsScheduled);
    }

    private static void StateBaselineSnapshotBuilder_EmitsBeginFramesAndComplete()
    {
        var builder = new StateBaselineSnapshotBuilder();
        LatestStateUpdate[] items =
        [
            new(1001, 10, new SubscriptionKey(11), PacketClass.Realtime, ImmutablePayloadBuffer.CopyFrom([1, 2])),
            new(1002, 11, new SubscriptionKey(12), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([3, 4, 5])),
        ];
        StateBaselineDescriptor descriptor = new(
            BaselineId: 7,
            HighestLatestStateSequence: 11,
            LastOrderedEventSequence: 42,
            ItemCount: 2);
        var output = new OutboundDatagram[8];

        int written = builder.Build(new ConnectionId(9), descriptor, items, output);

        Assert.Equal(4, written);
        Assert.Equal(StateBaselineBeginPacket.PacketId, DecodeHeader(output[0]).PacketId);
        Assert.Equal(StateBaselineFramePacket.PacketId, DecodeHeader(output[1]).PacketId);
        Assert.Equal(StateBaselineFramePacket.PacketId, DecodeHeader(output[2]).PacketId);
        Assert.Equal(StateBaselineCompletePacket.PacketId, DecodeHeader(output[3]).PacketId);

        ReadOnlySpan<byte> beginPayload = DecodePayload(output[0]);
        var beginReader = new BitReader(beginPayload);
        StateBaselineBeginPacket begin = StateBaselineBeginPacket.Deserialize(ref beginReader);
        Assert.Equal(descriptor, begin.Descriptor);

        ReadOnlySpan<byte> framePayload = DecodePayload(output[1]);
        var frameReader = new BitReader(framePayload);
        StateBaselineFramePacket frame = StateBaselineFramePacket.Deserialize(ref frameReader);
        Assert.Equal((uint)7, frame.BaselineId);
        Assert.Equal((ulong)1001, frame.ItemKey);
        Assert.Equal((ulong)10, frame.Sequence);

        ReadOnlySpan<byte> completePayload = DecodePayload(output[3]);
        var completeReader = new BitReader(completePayload);
        StateBaselineCompletePacket complete = StateBaselineCompletePacket.Deserialize(ref completeReader);
        Assert.Equal((uint)7, complete.BaselineId);
    }

    private static void ConnectionRecoveryPlanner_UsesSchedulerWhenNoRecoveryIsRequired()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        latest.Publish(100, new SubscriptionKey(1), ImmutablePayloadBuffer.CopyFrom([1]));
        ordered.Publish(new SubscriptionKey(2), ImmutablePayloadBuffer.CopyFrom([2]));
        var scheduler = new ConnectionStreamScheduler(latest, ordered);
        var planner = new ConnectionRecoveryPlanner(latest, ordered, scheduler);

        var scheduled = new ScheduledWorkItem[4];
        var snapshotItems = new LatestStateUpdate[4];
        var baseline = new OutboundDatagram[8];
        ConnectionRecoveryPlanResult result = planner.Plan(
            new ConnectionId(9),
            default,
            ConnectionSendBudget.Unlimited,
            scheduled,
            snapshotItems,
            baseline);

        Assert.Equal(ConnectionRecoveryMode.Scheduled, result.Mode);
        Assert.Equal(2, result.ScheduledWorkCount);
        Assert.Equal(0, result.BaselineDatagramCount);
        Assert.False(result.OrderedEventRecoveryRequired);
        Assert.Equal((ulong)2, result.NextState.OrderedEventCursor.NextSequence);
    }

    private static void ConnectionRecoveryPlanner_BuildsFullBaselineWhenOrderedEventRecoveryIsRequired()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 2);
        ulong latestSequence = latest.Publish(1001, new SubscriptionKey(11), ImmutablePayloadBuffer.CopyFrom([1, 2]));
        ordered.Publish(new SubscriptionKey(21), ImmutablePayloadBuffer.CopyFrom([3]));
        ordered.Publish(new SubscriptionKey(22), ImmutablePayloadBuffer.CopyFrom([4]));
        ulong newestEvent = ordered.Publish(new SubscriptionKey(23), ImmutablePayloadBuffer.CopyFrom([5]));
        var planner = new ConnectionRecoveryPlanner(latest, ordered);

        var scheduled = new ScheduledWorkItem[4];
        var snapshotItems = new LatestStateUpdate[4];
        var baseline = new OutboundDatagram[8];
        ConnectionRecoveryPlanResult result = planner.Plan(
            new ConnectionId(10),
            new ConnectionScheduleState(default, new OrderedEventCursor(1)),
            ConnectionSendBudget.Unlimited,
            scheduled,
            snapshotItems,
            baseline);

        Assert.Equal(ConnectionRecoveryMode.Baseline, result.Mode);
        Assert.Equal(0, result.ScheduledWorkCount);
        Assert.Equal(3, result.BaselineDatagramCount);
        Assert.True(result.OrderedEventRecoveryRequired);
        Assert.Equal(latestSequence, result.BaselineDescriptor.HighestLatestStateSequence);
        Assert.Equal(newestEvent, result.BaselineDescriptor.LastOrderedEventSequence);
        Assert.Equal(latestSequence, result.NextState.LatestStateCursor.LastSeenSequence);
        Assert.Equal(newestEvent + 1, result.NextState.OrderedEventCursor.NextSequence);
        Assert.Equal(StateBaselineBeginPacket.PacketId, DecodeHeader(baseline[0]).PacketId);
        Assert.Equal(StateBaselineCompletePacket.PacketId, DecodeHeader(baseline[2]).PacketId);
    }

    private static void ConnectionRecoveryExecutionBridge_RetransmitsBufferedEntriesWithinWindow()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        var recoveryPlanner = new ConnectionRecoveryPlanner(latest, ordered);
        var replay = new ReliableEventReplayBuffer(capacityPerConnection: 8);
        var acks = new ReliableEventAckTracker();
        ConnectionId connectionId = new(18);
        replay.Track(connectionId, sentTick: 100, sequence: 10, new SubscriptionKey(1), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([1]));
        replay.Track(connectionId, sentTick: 101, sequence: 11, new SubscriptionKey(2), PacketClass.Bulk, ImmutablePayloadBuffer.CopyFrom([2, 3]));
        acks.TryAcknowledge(connectionId, 9, out _);

        var bridge = new ConnectionRecoveryExecutionBridge(
            new ReliableEventRetransmitPlanner(replay, acks),
            recoveryPlanner);

        var replayScratch = new ReliableEventReplayEntry[4];
        var snapshotScratch = new LatestStateUpdate[4];
        var output = new OutboundDatagram[4];
        ConnectionRecoveryExecutionResult result = bridge.Execute(
            connectionId,
            nowTick: 103,
            new ReliableEventRetransmitPolicy(RetransmitThresholdTicks: 3, MaxRetransmitItemsPerTick: 4),
            replayScratch,
            snapshotScratch,
            output);

        Assert.Equal(ConnectionRecoveryMode.Retransmit, result.Mode);
        Assert.Equal(2, result.ReplayEntryCount);
        Assert.Equal(2, result.DatagramCount);
        Assert.Equal(PacketClass.ReliableEvent, output[0].PacketClass);
        Assert.Equal(PacketClass.Bulk, output[1].PacketClass);

        ReadOnlySpan<byte> eventPayload0 = DecodePayload(output[0]);
        var reader0 = new BitReader(eventPayload0);
        ReliableEventFramePacket frame0 = ReliableEventFramePacket.Deserialize(ref reader0);
        Assert.Equal((ulong)10, frame0.Sequence);

        ReadOnlySpan<byte> eventPayload1 = DecodePayload(output[1]);
        var reader1 = new BitReader(eventPayload1);
        ReliableEventFramePacket frame1 = ReliableEventFramePacket.Deserialize(ref reader1);
        Assert.Equal((ulong)11, frame1.Sequence);
    }

    private static void ConnectionRecoveryExecutionBridge_FallsBackToFullBaselineWhenReplayWindowIsBehind()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        ulong latestSequence = latest.Publish(1001, new SubscriptionKey(11), ImmutablePayloadBuffer.CopyFrom([9, 9]));
        ordered.Publish(new SubscriptionKey(21), ImmutablePayloadBuffer.CopyFrom([1]));
        ordered.Publish(new SubscriptionKey(22), ImmutablePayloadBuffer.CopyFrom([2]));
        ulong newestEvent = ordered.Publish(new SubscriptionKey(23), ImmutablePayloadBuffer.CopyFrom([3]));

        var replay = new ReliableEventReplayBuffer(capacityPerConnection: 2);
        var acks = new ReliableEventAckTracker();
        ConnectionId connectionId = new(19);
        replay.Track(connectionId, sentTick: 100, sequence: 20, new SubscriptionKey(1), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([1]));
        replay.Track(connectionId, sentTick: 101, sequence: 21, new SubscriptionKey(2), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([2]));
        replay.Track(connectionId, sentTick: 102, sequence: 22, new SubscriptionKey(3), PacketClass.ReliableEvent, ImmutablePayloadBuffer.CopyFrom([3]));
        acks.TryAcknowledge(connectionId, 19, out _);

        var bridge = new ConnectionRecoveryExecutionBridge(
            new ReliableEventRetransmitPlanner(replay, acks),
            new ConnectionRecoveryPlanner(latest, ordered));

        var replayScratch = new ReliableEventReplayEntry[4];
        var snapshotScratch = new LatestStateUpdate[4];
        var output = new OutboundDatagram[8];
        ConnectionRecoveryExecutionResult result = bridge.Execute(
            connectionId,
            nowTick: 105,
            ReliableEventRetransmitPolicy.Default,
            replayScratch,
            snapshotScratch,
            output);

        Assert.Equal(ConnectionRecoveryMode.Baseline, result.Mode);
        Assert.Equal(0, result.ReplayEntryCount);
        Assert.Equal(3, result.DatagramCount);
        Assert.Equal(latestSequence, result.BaselineDescriptor.HighestLatestStateSequence);
        Assert.Equal(newestEvent, result.BaselineDescriptor.LastOrderedEventSequence);
        Assert.Equal(latestSequence, result.NextState.LatestStateCursor.LastSeenSequence);
        Assert.Equal(newestEvent + 1, result.NextState.OrderedEventCursor.NextSequence);
        Assert.Equal(StateBaselineBeginPacket.PacketId, DecodeHeader(output[0]).PacketId);
        Assert.Equal(StateBaselineCompletePacket.PacketId, DecodeHeader(output[2]).PacketId);
    }

    private static void ConnectionLatestStateDeltaExecutionBridge_EmitsDeltaFramesAndMarksSentState()
    {
        var sentTracker = new ConnectionLatestStateSentTracker();
        var planner = new ConnectionLatestStateDeltaPlanner(sentTracker);
        var builder = new StateDeltaSnapshotBuilder(sentTracker);
        var bridge = new ConnectionLatestStateDeltaExecutionBridge(planner, builder);
        ConnectionId connectionId = new(34);
        StateBaselineDescriptor baseline = new(
            BaselineId: 9,
            HighestLatestStateSequence: 20,
            LastOrderedEventSequence: 40,
            ItemCount: 2);
        LatestStateUpdate[] snapshot =
        [
            new(
                ItemKey: 1001,
                Sequence: 10,
                ScopeKey: new SubscriptionKey(11),
                PacketClass: PacketClass.Realtime,
                Payload: ImmutablePayloadBuffer.CopyFrom([1])),
            new(
                ItemKey: 1002,
                Sequence: 11,
                ScopeKey: new SubscriptionKey(12),
                PacketClass: PacketClass.Bulk,
                Payload: ImmutablePayloadBuffer.CopyFrom([2, 3])),
        ];
        sentTracker.MarkSent(connectionId, snapshot[0]);

        var deltaScratch = new LatestStateUpdate[4];
        var output = new OutboundDatagram[4];
        ConnectionLatestStateDeltaExecutionResult result = bridge.Execute(
            connectionId,
            baseline,
            snapshot,
            deltaScratch,
            output);

        Assert.Equal(1, result.DeltaItemCount);
        Assert.Equal(1, result.DatagramCount);
        Assert.Equal((uint)9, result.BaselineId);
        Assert.Equal((ulong)11, result.HighestSequence);
        Assert.Equal(PacketClass.Bulk, output[0].PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, output[0].Channel);

        ReadOnlySpan<byte> payload = DecodePayload(output[0]);
        var reader = new BitReader(payload);
        StateDeltaFramePacket frame = StateDeltaFramePacket.Deserialize(ref reader);
        Assert.Equal((uint)9, frame.BaselineId);
        Assert.Equal((ulong)1002, frame.ItemKey);
        Assert.Equal((ulong)11, frame.Sequence);
        Assert.True(frame.Payload.Span.SequenceEqual(new byte[] { 2, 3 }));
        Assert.True(sentTracker.TryGetLastSentSequence(connectionId, 1002, out ulong sentSequence));
        Assert.Equal((ulong)11, sentSequence);
    }

    private static void OutboundDatagramAssembler_EncodesScheduledWorkIntoTypedFrames()
    {
        var assembler = new OutboundDatagramAssembler();
        ScheduledWorkItem[] items =
        [
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 10,
                ItemKey: 55,
                ScopeKey: default,
                Payload: ImmutablePayloadBuffer.CopyFrom([1, 2, 3])),
            new(
                ScheduledWorkKind.OrderedEvent,
                PacketClass.ReliableEvent,
                Sequence: 20,
                ItemKey: 0,
                ScopeKey: default,
                Payload: ImmutablePayloadBuffer.CopyFrom([9, 8]))
        ];

        var output = new OutboundDatagram[2];
        OutboundAssemblyResult result = assembler.Assemble(new ConnectionId(77), items, output);

        Assert.Equal(2, result.Count);
        Assert.Equal(NetworkPlane.State, output[0].Plane);
        Assert.Equal(PacketClass.Realtime, output[0].PacketClass);
        Assert.Equal(ChannelType.UnreliableSequenced, output[0].Channel);
        Assert.True(PacketCodec.TryDecodeEnvelope(output[0].Payload.Span, out PacketEnvelopeHeader realtimeHeader, out ReadOnlySpan<byte> realtimePayload));
        Assert.Equal(RealtimeStateFramePacket.PacketId, realtimeHeader.PacketId);
        Assert.Equal(PacketHeaderFlags.HighPriorityHint, realtimeHeader.Flags);
        var realtimeReader = new BitReader(realtimePayload);
        RealtimeStateFramePacket realtimeFrame = RealtimeStateFramePacket.Deserialize(ref realtimeReader);
        Assert.Equal((ulong)10, realtimeFrame.Sequence);
        Assert.Equal((ulong)55, realtimeFrame.ItemKey);
        Assert.True(realtimeFrame.Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 }));

        Assert.Equal(PacketClass.ReliableEvent, output[1].PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, output[1].Channel);
        Assert.True(PacketCodec.TryDecodeEnvelope(output[1].Payload.Span, out PacketEnvelopeHeader eventHeader, out ReadOnlySpan<byte> eventPayload));
        Assert.Equal(ReliableEventFramePacket.PacketId, eventHeader.PacketId);
        Assert.Equal(PacketHeaderFlags.None, eventHeader.Flags);
        var eventReader = new BitReader(eventPayload);
        ReliableEventFramePacket eventFrame = ReliableEventFramePacket.Deserialize(ref eventReader);
        Assert.Equal((ulong)20, eventFrame.Sequence);
        Assert.True(eventFrame.Payload.Span.SequenceEqual(new byte[] { 9, 8 }));
    }

    private static void OutboundDatagramAssembler_AppliesTransformsAndHeaderFlags()
    {
        var assembler = new OutboundDatagramAssembler(new MarkCompressedTransform());
        ScheduledWorkItem[] items =
        [
            new(
                ScheduledWorkKind.OrderedEvent,
                PacketClass.ReliableEvent,
                Sequence: 30,
                ItemKey: 0,
                ScopeKey: default,
                Payload: ImmutablePayloadBuffer.CopyFrom([4, 5]))
        ];

        var output = new OutboundDatagram[1];
        OutboundAssemblyResult result = assembler.Assemble(new ConnectionId(88), items, output);

        Assert.Equal(1, result.Count);
        Assert.Equal(1, result.CompressedCount);
        Assert.True(output[0].Payload.Length >= PacketEnvelopeHeader.SizeInBytes);
        var reader = new BitReader(output[0].Payload.Span);
        PacketEnvelopeHeader header = PacketEnvelopeHeader.Deserialize(ref reader);
        Assert.Equal(PacketHeaderFlags.Compressed, header.Flags);
        Assert.Equal(ReliableEventFramePacket.PacketId, header.PacketId);
        Assert.Equal((ushort)(output[0].Payload.Length - PacketEnvelopeHeader.SizeInBytes), header.PayloadLength);
        Assert.Equal((byte)255, output[0].Payload.Span[^1]);
    }

    private static void ConnectionSubscriptionRegistry_TracksSubscriptionsAndRemovals()
    {
        var registry = new ConnectionSubscriptionRegistry();
        SubscriptionKey keyA = new(100);
        SubscriptionKey keyB = new(200);

        Assert.True(registry.Subscribe(new ConnectionId(1), keyA));
        Assert.False(registry.Subscribe(new ConnectionId(1), keyA));
        Assert.True(registry.Subscribe(new ConnectionId(2), keyA));
        Assert.True(registry.Subscribe(new ConnectionId(1), keyB));

        Assert.Equal(2, registry.CountSubscribers(keyA));
        Assert.Equal(1, registry.CountSubscribers(keyB));

        var subscribers = new ConnectionId[4];
        int count = registry.CopySubscribers(keyA, subscribers);
        Assert.Equal(2, count);
        Assert.True(Array.Exists(subscribers, static c => c.Value == 1));
        Assert.True(Array.Exists(subscribers, static c => c.Value == 2));

        Assert.True(registry.Unsubscribe(new ConnectionId(2), keyA));
        Assert.Equal(1, registry.CountSubscribers(keyA));
        Assert.Equal(2, registry.RemoveConnection(new ConnectionId(1)));
        Assert.Equal(0, registry.CountSubscribers(keyB));
    }

    private static void SharedFramePublisher_CreatesTypedPublishedFrames()
    {
        ImmutablePayloadBuffer payload = ImmutablePayloadBuffer.CopyFrom([7, 8]);
        PublishedFrame latest = SharedFramePublisher.PublishLatestState(12, 44, payload);
        Assert.Equal(RealtimeStateFramePacket.PacketId, latest.PacketId);
        Assert.Equal(NetworkPlane.State, latest.Plane);
        Assert.Equal(PacketClass.Realtime, latest.PacketClass);
        Assert.Equal(ChannelType.UnreliableSequenced, latest.Channel);
        Assert.Equal(PacketHeaderFlags.HighPriorityHint, latest.DefaultHeaderFlags);

        var latestReader = new BitReader(latest.Body.Span);
        RealtimeStateFramePacket latestFrame = RealtimeStateFramePacket.Deserialize(ref latestReader);
        Assert.Equal((ulong)12, latestFrame.Sequence);
        Assert.Equal((ulong)44, latestFrame.ItemKey);
        Assert.True(latestFrame.Payload.Span.SequenceEqual(new byte[] { 7, 8 }));

        PublishedFrame ordered = SharedFramePublisher.PublishOrderedEvent(21, payload);
        Assert.Equal(ReliableEventFramePacket.PacketId, ordered.PacketId);
        Assert.Equal(PacketClass.ReliableEvent, ordered.PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, ordered.Channel);

        PublishedFrame bulkLatest = SharedFramePublisher.PublishLatestState(22, 45, payload, PacketClass.Bulk);
        Assert.Equal(PacketClass.Bulk, bulkLatest.PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, bulkLatest.Channel);
        Assert.Equal(PacketHeaderFlags.None, bulkLatest.DefaultHeaderFlags);

        PublishedFrame bulkOrdered = SharedFramePublisher.PublishOrderedEvent(23, payload, PacketClass.Bulk);
        Assert.Equal(PacketClass.Bulk, bulkOrdered.PacketClass);
        Assert.Equal(ChannelType.ReliableOrdered, bulkOrdered.Channel);
    }

    private static void ScheduledWorkPublishedFrameProjector_ProjectsScopedFrames()
    {
        ScheduledWorkItem[] items =
        [
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 40,
                ItemKey: 900,
                ScopeKey: new SubscriptionKey(11),
                Payload: ImmutablePayloadBuffer.CopyFrom([1, 2])),
            new(
                ScheduledWorkKind.OrderedEvent,
                PacketClass.ReliableEvent,
                Sequence: 41,
                ItemKey: 0,
                ScopeKey: new SubscriptionKey(22),
                Payload: ImmutablePayloadBuffer.CopyFrom([3, 4]))
        ];
        SubscriptionKey[] scopes = [new SubscriptionKey(11), new SubscriptionKey(22)];
        var projected = new ScopedPublishedFrame[2];

        int count = ScheduledWorkPublishedFrameProjector.Project(items, scopes, projected);

        Assert.Equal(2, count);
        Assert.Equal((ulong)11, projected[0].ScopeKey.Value);
        Assert.Equal(RealtimeStateFramePacket.PacketId, projected[0].Frame.PacketId);
        Assert.Equal((ulong)22, projected[1].ScopeKey.Value);
        Assert.Equal(ReliableEventFramePacket.PacketId, projected[1].Frame.PacketId);
    }

    private static void OutboundDatagramAssembler_Fanout_AssemblesSharedFrameForSubscribers()
    {
        var registry = new ConnectionSubscriptionRegistry();
        SubscriptionKey key = new(300);
        registry.Subscribe(new ConnectionId(10), key);
        registry.Subscribe(new ConnectionId(11), key);
        registry.Subscribe(new ConnectionId(12), key);

        var subscribers = new ConnectionId[4];
        int subscriberCount = registry.CopySubscribers(key, subscribers);
        Assert.Equal(3, subscriberCount);

        PublishedFrame frame = SharedFramePublisher.PublishLatestState(
            sequence: 99,
            itemKey: 500,
            payload: ImmutablePayloadBuffer.CopyFrom([1, 2, 3, 4]));
        var assembler = new OutboundDatagramAssembler();
        var output = new OutboundDatagram[4];
        OutboundAssemblyResult result = assembler.AssembleFanout(subscribers.AsSpan(0, subscriberCount), frame, output);

        Assert.Equal(3, result.Count);
        for (int i = 0; i < result.Count; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(output[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            Assert.Equal(RealtimeStateFramePacket.PacketId, header.PacketId);
            var reader = new BitReader(payload);
            RealtimeStateFramePacket decoded = RealtimeStateFramePacket.Deserialize(ref reader);
            Assert.Equal((ulong)99, decoded.Sequence);
            Assert.Equal((ulong)500, decoded.ItemKey);
            Assert.True(decoded.Payload.Span.SequenceEqual(new byte[] { 1, 2, 3, 4 }));
        }
    }

    private static void SubscriptionFanoutDispatcher_DispatchesScopedFramesToSubscribers()
    {
        var registry = new ConnectionSubscriptionRegistry();
        SubscriptionKey latestKey = new(500);
        SubscriptionKey eventKey = new(600);
        registry.Subscribe(new ConnectionId(1), latestKey);
        registry.Subscribe(new ConnectionId(2), latestKey);
        registry.Subscribe(new ConnectionId(2), eventKey);
        registry.Subscribe(new ConnectionId(3), eventKey);

        ScopedPublishedFrame[] frames =
        [
            new(
                latestKey,
                SharedFramePublisher.PublishLatestState(70, 701, ImmutablePayloadBuffer.CopyFrom([7]))),
            new(
                eventKey,
                SharedFramePublisher.PublishOrderedEvent(80, ImmutablePayloadBuffer.CopyFrom([8, 9])))
        ];

        var dispatcher = new SubscriptionFanoutDispatcher(registry, new OutboundDatagramAssembler());
        var subscriberScratch = new ConnectionId[8];
        var output = new OutboundDatagram[8];
        SubscriptionFanoutDispatchResult result = dispatcher.Dispatch(frames, subscriberScratch, output);

        Assert.Equal(2, result.FrameCount);
        Assert.Equal(4, result.SubscriberDeliveries);
        Assert.Equal(4, result.DatagramCount);

        int latestCount = 0;
        int eventCount = 0;
        for (int i = 0; i < result.DatagramCount; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(output[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            if (header.PacketId == RealtimeStateFramePacket.PacketId)
            {
                latestCount++;
                var reader = new BitReader(payload);
                RealtimeStateFramePacket frame = RealtimeStateFramePacket.Deserialize(ref reader);
                Assert.Equal((ulong)70, frame.Sequence);
            }
            else if (header.PacketId == ReliableEventFramePacket.PacketId)
            {
                eventCount++;
                var reader = new BitReader(payload);
                ReliableEventFramePacket frame = ReliableEventFramePacket.Deserialize(ref reader);
                Assert.Equal((ulong)80, frame.Sequence);
            }
        }

        Assert.Equal(2, latestCount);
        Assert.Equal(2, eventCount);
    }

    private static void SubscriptionFanoutDispatcher_DispatchScheduledWork_ToSubscribers()
    {
        var registry = new ConnectionSubscriptionRegistry();
        SubscriptionKey latestKey = new(650);
        SubscriptionKey eventKey = new(651);
        registry.Subscribe(new ConnectionId(1), latestKey);
        registry.Subscribe(new ConnectionId(2), latestKey);
        registry.Subscribe(new ConnectionId(2), eventKey);
        registry.Subscribe(new ConnectionId(3), eventKey);

        ScheduledWorkItem[] items =
        [
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 81,
                ItemKey: 810,
                ScopeKey: latestKey,
                Payload: ImmutablePayloadBuffer.CopyFrom([8])),
            new(
                ScheduledWorkKind.OrderedEvent,
                PacketClass.ReliableEvent,
                Sequence: 82,
                ItemKey: 0,
                ScopeKey: eventKey,
                Payload: ImmutablePayloadBuffer.CopyFrom([9, 10]))
        ];
        var dispatcher = new SubscriptionFanoutDispatcher(registry, new OutboundDatagramAssembler());
        var subscriberScratch = new ConnectionId[8];
        var output = new OutboundDatagram[8];

        SubscriptionFanoutDispatchResult result = dispatcher.DispatchScheduledWork(items, subscriberScratch, output);

        Assert.Equal(2, result.FrameCount);
        Assert.Equal(4, result.SubscriberDeliveries);
        Assert.Equal(4, result.DatagramCount);

        int latestCount = 0;
        int eventCount = 0;
        for (int i = 0; i < result.DatagramCount; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(output[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            if (header.PacketId == RealtimeStateFramePacket.PacketId)
            {
                latestCount++;
                var reader = new BitReader(payload);
                RealtimeStateFramePacket frame = RealtimeStateFramePacket.Deserialize(ref reader);
                Assert.Equal((ulong)81, frame.Sequence);
                Assert.Equal((ulong)810, frame.ItemKey);
            }
            else if (header.PacketId == ReliableEventFramePacket.PacketId)
            {
                eventCount++;
                var reader = new BitReader(payload);
                ReliableEventFramePacket frame = ReliableEventFramePacket.Deserialize(ref reader);
                Assert.Equal((ulong)82, frame.Sequence);
            }
        }

        Assert.Equal(2, latestCount);
        Assert.Equal(2, eventCount);
    }

    private static void SubscriptionFanoutDispatcher_DispatchScheduledWork_AppliesLatestStateRecipientPhasing()
    {
        var registry = new ConnectionSubscriptionRegistry();
        SubscriptionKey scopeKey = new(651);
        ConnectionId[] subscribers = [new ConnectionId(1), new ConnectionId(2), new ConnectionId(3), new ConnectionId(4)];
        for (int i = 0; i < subscribers.Length; i++)
            registry.Subscribe(subscribers[i], scopeKey);

        ScheduledWorkItem[] items =
        [
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 82,
                ItemKey: 810,
                ScopeKey: scopeKey,
                Payload: ImmutablePayloadBuffer.CopyFrom([8]),
                DeliveryCadenceTicks: 2)
        ];
        var dispatcher = new SubscriptionFanoutDispatcher(
            registry,
            new OutboundDatagramAssembler(),
            enableLatestStateRecipientPhasing: true);
        var subscriberScratch = new ConnectionId[8];
        var output = new OutboundDatagram[8];

        SubscriptionFanoutDispatchResult result = dispatcher.DispatchScheduledWork(items, subscriberScratch, output);

        int expectedCount = 0;
        for (int i = 0; i < subscribers.Length; i++)
        {
            if (ShouldDeliverPhasedLatestStateForTest(810, 82, subscribers[i], 2))
                expectedCount++;
        }

        Assert.Equal(1, result.FrameCount);
        Assert.Equal(expectedCount, result.SubscriberDeliveries);
        Assert.Equal(expectedCount, result.DatagramCount);

        for (int i = 0; i < result.DatagramCount; i++)
        {
            Assert.True(ShouldDeliverPhasedLatestStateForTest(810, 82, output[i].ConnectionId, 2));
            Assert.True(PacketCodec.TryDecodeEnvelope(output[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            Assert.Equal(RealtimeStateFramePacket.PacketId, header.PacketId);
            var reader = new BitReader(payload);
            RealtimeStateFramePacket frame = RealtimeStateFramePacket.Deserialize(ref reader);
            Assert.Equal((ulong)82, frame.Sequence);
            Assert.Equal((ulong)810, frame.ItemKey);
        }
    }

    private static void SubscriptionFanoutDispatcher_DispatchScheduledWork_BatchesContiguousRealtimeLatestStateItems()
    {
        var registry = new ConnectionSubscriptionRegistry();
        SubscriptionKey scopeKey = new(652);
        registry.Subscribe(new ConnectionId(1), scopeKey);
        registry.Subscribe(new ConnectionId(2), scopeKey);

        ScheduledWorkItem[] items =
        [
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 81,
                ItemKey: 810,
                ScopeKey: scopeKey,
                Payload: ImmutablePayloadBuffer.CopyFrom([8])),
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 82,
                ItemKey: 811,
                ScopeKey: scopeKey,
                Payload: ImmutablePayloadBuffer.CopyFrom([9, 10])),
        ];
        var dispatcher = new SubscriptionFanoutDispatcher(
            registry,
            new OutboundDatagramAssembler(),
            enableLatestStateBatching: true);
        var subscriberScratch = new ConnectionId[8];
        var output = new OutboundDatagram[8];

        SubscriptionFanoutDispatchResult result = dispatcher.DispatchScheduledWork(items, subscriberScratch, output);

        Assert.Equal(1, result.FrameCount);
        Assert.Equal(2, result.SubscriberDeliveries);
        Assert.Equal(2, result.DatagramCount);

        for (int i = 0; i < result.DatagramCount; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(output[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            Assert.Equal(RealtimeStateBatchPacket.PacketId, header.PacketId);
            var reader = new BitReader(payload);
            RealtimeStateBatchPacket batch = RealtimeStateBatchPacket.Deserialize(ref reader);
            Assert.Equal(2, batch.Items.Length);
            Assert.Equal((ulong)81, batch.Items[0].Sequence);
            Assert.Equal((ulong)810, batch.Items[0].ItemKey);
            Assert.True(batch.Items[0].Payload.Span.SequenceEqual(new byte[] { 8 }));
            Assert.Equal((ulong)82, batch.Items[1].Sequence);
            Assert.Equal((ulong)811, batch.Items[1].ItemKey);
            Assert.True(batch.Items[1].Payload.Span.SequenceEqual(new byte[] { 9, 10 }));
        }
    }

    private static void SubscriptionFanoutDispatcher_DispatchScheduledWork_AppliesLatestStateConnectionPhasing()
    {
        var registry = new ConnectionSubscriptionRegistry();
        SubscriptionKey scopeKey = new(653);
        const int subscriberCount = 16;
        for (int i = 1; i <= subscriberCount; i++)
            registry.Subscribe(new ConnectionId(i), scopeKey);

        var dispatcher = new SubscriptionFanoutDispatcher(
            registry,
            new OutboundDatagramAssembler(),
            latestStateConnectionPhasingCadenceTicks: 2);
        var subscriberScratch = new ConnectionId[subscriberCount];
        var tickZeroOutput = new OutboundDatagram[subscriberCount];
        var tickOneOutput = new OutboundDatagram[subscriberCount];

        ScheduledWorkItem[] tickZeroItems =
        [
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 91,
                ItemKey: 910,
                ScopeKey: scopeKey,
                Payload: ImmutablePayloadBuffer.CopyFrom([9]),
                PublishTick: 0)
        ];
        ScheduledWorkItem[] tickOneItems =
        [
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 92,
                ItemKey: 910,
                ScopeKey: scopeKey,
                Payload: ImmutablePayloadBuffer.CopyFrom([9]),
                PublishTick: 1)
        ];

        SubscriptionFanoutDispatchResult tickZero = dispatcher.DispatchScheduledWork(tickZeroItems, subscriberScratch, tickZeroOutput);
        SubscriptionFanoutDispatchResult tickOne = dispatcher.DispatchScheduledWork(tickOneItems, subscriberScratch, tickOneOutput);

        Assert.True(tickZero.DatagramCount > 0);
        Assert.True(tickZero.DatagramCount < subscriberCount);
        Assert.True(tickOne.DatagramCount > 0);
        Assert.True(tickOne.DatagramCount < subscriberCount);
        Assert.Equal(subscriberCount, tickZero.DatagramCount + tickOne.DatagramCount);

        var delivered = new HashSet<int>();
        for (int i = 0; i < tickZero.DatagramCount; i++)
        {
            Assert.True(delivered.Add(tickZeroOutput[i].ConnectionId.Value));
            tickZeroOutput[i].Dispose();
        }

        for (int i = 0; i < tickOne.DatagramCount; i++)
        {
            Assert.True(delivered.Add(tickOneOutput[i].ConnectionId.Value));
            tickOneOutput[i].Dispose();
        }
    }

    private static bool ShouldDeliverPhasedLatestStateForTest(ulong itemKey, ulong sequence, ConnectionId connectionId, uint cadenceTicks)
    {
        if (cadenceTicks <= 1 || !connectionId.IsValid)
            return true;

        ulong tickMod = sequence % cadenceTicks;
        unchecked
        {
            ulong hash = itemKey * 11400714819323198485UL;
            hash ^= (uint)connectionId.Value * 7046029254386353131UL;
            return tickMod == (hash % cadenceTicks);
        }
    }

    private static void ScheduledWorkFanoutBridge_ProjectsAndDispatchesInOnePass()
    {
        var registry = new ConnectionSubscriptionRegistry();
        SubscriptionKey latestKey = new(700);
        SubscriptionKey eventKey = new(701);
        registry.Subscribe(new ConnectionId(1), latestKey);
        registry.Subscribe(new ConnectionId(2), latestKey);
        registry.Subscribe(new ConnectionId(2), eventKey);

        ScheduledWorkItem[] items =
        [
            new(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: 90,
                ItemKey: 900,
                ScopeKey: latestKey,
                Payload: ImmutablePayloadBuffer.CopyFrom([1, 2])),
            new(
                ScheduledWorkKind.OrderedEvent,
                PacketClass.ReliableEvent,
                Sequence: 91,
                ItemKey: 0,
                ScopeKey: eventKey,
                Payload: ImmutablePayloadBuffer.CopyFrom([3]))
        ];
        var bridge = new ScheduledWorkFanoutBridge(registry);
        var subscribers = new ConnectionId[4];
        var output = new OutboundDatagram[4];

        ScheduledWorkFanoutResult result = bridge.Dispatch(items, subscribers, output);

        Assert.Equal(2, result.ProjectedFrameCount);
        Assert.Equal(3, result.SubscriberDeliveries);
        Assert.Equal(3, result.DatagramCount);

        int latestCount = 0;
        int eventCount = 0;
        for (int i = 0; i < result.DatagramCount; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(output[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            if (header.PacketId == RealtimeStateFramePacket.PacketId)
            {
                latestCount++;
                var reader = new BitReader(payload);
                RealtimeStateFramePacket frame = RealtimeStateFramePacket.Deserialize(ref reader);
                Assert.Equal((ulong)90, frame.Sequence);
                Assert.Equal((ulong)900, frame.ItemKey);
            }
            else if (header.PacketId == ReliableEventFramePacket.PacketId)
            {
                eventCount++;
                var reader = new BitReader(payload);
                ReliableEventFramePacket frame = ReliableEventFramePacket.Deserialize(ref reader);
                Assert.Equal((ulong)91, frame.Sequence);
            }
        }

        Assert.Equal(2, latestCount);
        Assert.Equal(1, eventCount);
    }

    private static void ConnectionStreamFanoutBridge_SchedulesAndDispatchesScopedStreamWork()
    {
        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        SubscriptionKey latestKey = new(801);
        SubscriptionKey eventKey = new(802);
        ulong latestSequence = latest.Publish(500, latestKey, ImmutablePayloadBuffer.CopyFrom([5, 0]));
        ulong eventSequence = ordered.Publish(eventKey, ImmutablePayloadBuffer.CopyFrom([8, 0]));

        var registry = new ConnectionSubscriptionRegistry();
        registry.Subscribe(new ConnectionId(1), latestKey);
        registry.Subscribe(new ConnectionId(2), latestKey);
        registry.Subscribe(new ConnectionId(2), eventKey);

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(latest, ordered),
            new ScheduledWorkFanoutBridge(registry));
        var scheduled = new ScheduledWorkItem[4];
        var subscribers = new ConnectionId[4];
        var output = new OutboundDatagram[4];

        ConnectionScheduledFanoutResult result = bridge.ScheduleAndDispatch(
            default,
            ConnectionSendBudget.Unlimited,
            scheduled,
            subscribers,
            output);

        Assert.Equal(2, result.Schedule.Count);
        Assert.False(result.Schedule.OrderedEventRecoveryRequired);
        Assert.Equal(3, result.Fanout.DatagramCount);
        Assert.Equal(3, result.Fanout.SubscriberDeliveries);
        Assert.Equal(latestSequence, scheduled[0].Sequence);
        Assert.Equal((ulong)801, scheduled[0].ScopeKey.Value);
        Assert.Equal(eventSequence, scheduled[1].Sequence);
        Assert.Equal((ulong)802, scheduled[1].ScopeKey.Value);
    }

    private static void ChunkAoiPolicy_AssignsUniqueNearestRingsByWorldSpaceOverlap()
    {
        var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, 7.5f, 23.5f);
        var scopes = new List<ChunkSubscriptionScope>();

        int count = policy.CollectScopes(new WorldPosition2(8f, 8f), scopes);

        Assert.True(count > 0);
        Assert.True(scopes.Exists(static s => s.Chunk == new ChunkCoord(0, 0) && s.RingIndex == 0));
        Assert.True(scopes.Exists(static s => s.Chunk == new ChunkCoord(1, 0) && s.RingIndex == 1));
        Assert.False(scopes.Exists(static s => s.Chunk == new ChunkCoord(1, 0) && s.RingIndex == 0));

        var keys = new HashSet<SubscriptionKey>();
        foreach (ChunkSubscriptionScope scope in scopes)
            Assert.True(keys.Add(scope.Key));
    }

    private static void ChunkAoiSubscriptionMaintainer_ReconcilesSubscriptionsAcrossMovement()
    {
        var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, 7.5f, 23.5f);
        var maintainer = new ChunkAoiSubscriptionMaintainer(policy);
        var registry = new ConnectionSubscriptionRegistry();
        var connectionId = new ConnectionId(44);

        AoiSubscriptionUpdateResult first = maintainer.UpdateConnection(
            connectionId,
            new WorldPosition2(8f, 8f),
            registry);

        Assert.True(first.Changed);
        Assert.True(first.AddedCount > 0);
        Assert.Equal(0, first.RemovedCount);

        AoiSubscriptionUpdateResult second = maintainer.UpdateConnection(
            connectionId,
            new WorldPosition2(8f, 8f),
            registry);

        Assert.False(second.Changed);
        Assert.Equal(0, second.AddedCount);
        Assert.Equal(0, second.RemovedCount);

        AoiSubscriptionUpdateResult moved = maintainer.UpdateConnection(
            connectionId,
            new WorldPosition2(24f, 8f),
            registry);

        Assert.True(moved.Changed);
        Assert.True(moved.AddedCount > 0 || moved.RemovedCount > 0);
        int removed = maintainer.RemoveConnection(connectionId, registry);
        Assert.Equal(moved.DesiredScopeCount, removed);
    }

    private static void PumpUntilHandshakeReady(
        NetworkControlPlaneHost server,
        NetworkControlPlaneHost client,
        long serverTicksStart,
        long clientTicksStart,
        int maxSteps = 16)
    {
        long serverTicks = serverTicksStart;
        long clientTicks = clientTicksStart;
        for (int i = 0; i < maxSteps; i++)
        {
            server.Pump(serverTicks++);
            client.Pump(clientTicks++);

            if (server.Sessions.TryGetSessionIdByConnection(new ConnectionId(1), out SessionId serverSessionId) &&
                server.Sessions.TryGetSnapshot(serverSessionId, out SessionSnapshot serverSnapshot) &&
                serverSnapshot.State == ConnectionSessionState.Ready &&
                client.Sessions.TryGetSessionIdByConnection(new ConnectionId(1), out SessionId clientSessionId) &&
                client.Sessions.TryGetSnapshot(clientSessionId, out SessionSnapshot clientSnapshot) &&
                clientSnapshot.State == ConnectionSessionState.Ready)
            {
                return;
            }
        }

        throw new InvalidOperationException("Handshake did not reach Ready state within the expected pump window.");
    }

    private static int PollUntilReceived(INetworkTransport transport, InboundDatagram[] destination, int maxAttempts)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            int count = transport.PollInbound(destination);
            if (count > 0)
                return count;

            Thread.Sleep(1);
        }

        return 0;
    }

    private static PacketEnvelopeHeader DecodeHeader(in OutboundDatagram datagram)
    {
        Assert.True(PacketCodec.TryDecodeEnvelope(datagram.Payload.Span, out PacketEnvelopeHeader header, out _));
        return header;
    }

    private static ReadOnlySpan<byte> DecodePayload(in OutboundDatagram datagram)
    {
        Assert.True(PacketCodec.TryDecodeEnvelope(datagram.Payload.Span, out _, out ReadOnlySpan<byte> payload));
        return payload;
    }

}

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
            throw new InvalidOperationException(message ?? "Expected condition to be false.");
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected '{expected}' but got '{actual}'.");
    }

    public static void NotNull(object? value, string? message = null)
    {
        if (value is null)
            throw new InvalidOperationException(message ?? "Expected value to be non-null.");
    }

    public static T NotNull<T>(T? value, string? message = null)
        where T : class
    {
        if (value is null)
            throw new InvalidOperationException(message ?? "Expected value to be non-null.");

        return value;
    }
}

internal sealed class MarkCompressedTransform : IOutboundTransform
{
    public OutboundTransformResult Transform(ImmutablePayloadBuffer payload, PacketHeaderFlags headerFlags, in OutboundTransformContext context)
    {
        byte[] data = new byte[payload.Length + 1];
        payload.Span.CopyTo(data);
        data[^1] = 255;
        return new OutboundTransformResult(
            ImmutablePayloadBuffer.FromOwned(data),
            headerFlags | PacketHeaderFlags.Compressed);
    }
}
